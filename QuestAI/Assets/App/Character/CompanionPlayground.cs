using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Toys in the room: placement, simple rolling/bouncing physics, block towers and carrying.
    // The character's play (walking over, pushing, stacking) is sequenced by MotionPerformer; the conversation stays connected.
    public sealed class CompanionPlayground : MonoBehaviour
    {
        public enum ToyKind { Ball, Blocks, Spinner }
        public sealed class Toy
        {
            public GameObject root; public SceneObject item; public ToyKind kind;
            public Vector3 velocity; public float spin; public bool rolling, carried, stacked;
            public readonly List<Transform> pieces = new List<Transform>();
            public Vector3 Position => root.transform.position;
            // Where the toy visibly is (the ball's centre, the middle of the blocks).
            public Vector3 Center => item.bounds.center;
            public float Speed => new Vector2(velocity.x, velocity.z).magnitude;
        }
        public const float BallRadius = .2f, Deceleration = .9f, BlockSize = .15f;
        public const int Blocks = 5, MaxToys = 8;
        readonly List<Toy> toys = new List<Toy>();
        readonly List<Material> materials = new List<Material>();
        CharacterBrain brain;
        ToyKind? placing;
        Toy selected;
        int serial;
        public IReadOnlyList<Toy> All => toys;
        public bool Busy => brain && brain.performer && brain.performer.Busy;
        public int Count => toys.Count;
        public string SelectedName => selected?.item.id ?? "なし";
        public Action<string> Status;
        // Things the user did to a toy, told to the conversation.
        public Action<string> Noticed;
        void Awake() { brain = GetComponent<CharacterBrain>(); }
        static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        public static string Label(ToyKind kind) => kind == ToyKind.Ball ? "ボール" : kind == ToyKind.Blocks ? "積み木" : "回転おもちゃ";
        public static ToyKind? KindOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Contains("ボール") || text.Contains("ball") || text.Contains("Ball")) return ToyKind.Ball;
            if (text.Contains("積み木") || text.Contains("積木") || text.Contains("ブロック") || text.Contains("block")) return ToyKind.Blocks;
            if (text.Contains("回転") || text.Contains("コマ") || text.Contains("こま") || text.Contains("spin")) return ToyKind.Spinner;
            return null;
        }
        public static bool CanPlace(SceneAwareness world, Vector3 point, Vector3 character, Vector3 user, IEnumerable<Vector3> existing)
        {
            if(world == null || world.grid.Blocked(point) || Distance(point,character)<1 || Distance(point,user)<.8f) return false;
            // Reserve the complete toy footprint, not only its central navigation cell.
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(world.grid.Blocked(point+new Vector3(x*.25f,0,z*.25f)))return false;
            return !existing.Any(p=>Distance(p,point)<.8f);
        }
        public void BeginPlacement(ToyKind kind)
        {
            placing=kind; Status?.Invoke("床をクリック／レーザーで選択して配置。最大8個。家具のない場所を選んでください。");
        }
        // A click on a toy plays with it (the user's own hand); a click on the floor places the chosen toy.
        public void PlaceFromRay(Ray ray)
        {
            if(!brain||brain.world==null)return;
            if(Touch(ray))return;
            if(!placing.HasValue)return;
            var plane=new Plane(Vector3.up,new Vector3(0,brain.world.floor.max.y,0));
            if(!plane.Raycast(ray,out float d)||d>8)return;
            // Do not place through furniture between the pointer and floor.
            if(Physics.Raycast(ray,out var hit,d)&&hit.point.y>brain.world.floor.max.y+.08f){Status?.Invoke("家具の手前の空いている床を選んでください。");return;}
            TryPlace(placing.Value,ray.GetPoint(d));
        }
        public Toy Hit(Ray ray)
        {
            Toy best=null;float nearest=8;
            foreach(var toy in toys){var b=toy.item.bounds;b.Expand(.1f);if(b.IntersectRay(ray,out float d)&&d<nearest){nearest=d;best=toy;}}
            return best;
        }
        public bool Touch(Ray ray)
        {
            var toy=Hit(ray);if(toy==null||toy.carried)return false;
            var push=Flat(ray.direction).normalized;if(push.sqrMagnitude<.01f)push=Flat(toy.Position-brain.user.position).normalized;
            string done;
            switch(toy.kind)
            {
                case ToyKind.Ball:Push(toy,push*2.2f);done="ユーザーが"+toy.item.id+"を転がしました。";break;
                case ToyKind.Blocks:if(toy.stacked){KnockDown(toy,push);done="ユーザーが"+toy.item.id+"の塔を崩しました。";}else{StartCoroutine(Stack(toy,Blocks,.25f));done="ユーザーが"+toy.item.id+"を積み上げました。";}break;
                default:Spin(toy,900);done="ユーザーが"+toy.item.id+"を回しました。";break;
            }
            selected=toy;Status?.Invoke(done.Replace("ユーザーが","あなたが"));Noticed?.Invoke(done);return true;
        }
        public bool TryPlace(ToyKind kind,Vector3 point)
        {
            if(!brain||brain.world==null||toys.Count>=MaxToys||
                !CanPlace(brain.world,point,brain.transform.position,brain.user.position,toys.Select(t=>t.Position)))
            {Status?.Invoke("ここには置けません。キャラクター・家具から離れた床を選んでください。");return false;}
            point.y=brain.world.floor.max.y;
            string label=Label(kind);
            var root=new GameObject(label);root.transform.position=point;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,gameObject.scene);
            var toy=new Toy{root=root,kind=kind,item=new SceneObject{id=label+(++serial),label=label,obstacle=true}};
            if(kind==ToyKind.Ball)Part(toy,PrimitiveType.Sphere,new Vector3(0,BallRadius,0),Vector3.one*BallRadius*2,new Color(.95f,.37f,.18f));
            else if(kind==ToyKind.Blocks)
            {
                // Scattered loosely on the floor so there is something to stack.
                for(int i=0;i<Blocks;i++)
                {
                    float angle=i*72+serial*17;var at=Quaternion.Euler(0,angle,0)*Vector3.forward*(i==0?0:.22f);
                    var piece=Part(toy,PrimitiveType.Cube,at+Vector3.up*BlockSize/2,Vector3.one*BlockSize,Color.HSVToRGB(i/(float)Blocks*.8f+.05f,.6f,.95f));piece.localRotation=Quaternion.Euler(0,angle*1.7f,0);
                }
            }
            else {Part(toy,PrimitiveType.Cylinder,new Vector3(0,.05f,0),new Vector3(.4f,.05f,.4f),new Color(.4f,.25f,.65f));Part(toy,PrimitiveType.Cube,new Vector3(0,.2f,0),new Vector3(.4f,.08f,.12f),new Color(1,.72f,.22f));}
            Sync(toy);toys.Add(toy);brain.world.objects.Add(toy.item);RebuildGrid();selected=toy;placing=null;
            Status?.Invoke(toy.item.id+"を置きました。声や文字で遊び方を頼んでみよう。");return true;
        }
        // Puts a new toy on free floor in front of the character, preferring the space between it and the user.
        public Toy Spawn(ToyKind kind)
        {
            if(!brain||brain.world==null||toys.Count>=MaxToys)return null;
            Vector3 self=brain.transform.position,user=brain.user.position,ideal=self+brain.transform.forward*1.2f;
            ideal=Vector3.Lerp(ideal,(self+user)/2,.4f);
            foreach(var cell in brain.world.grid.FreeCells().OrderBy(c=>Distance(c,ideal)))
                if(CanPlace(brain.world,cell,self,user,toys.Select(t=>t.Position))&&TryPlace(kind,cell))return toys[toys.Count-1];
            return null;
        }
        Transform Part(Toy toy,PrimitiveType shape,Vector3 at,Vector3 scale,Color color)
        {
            var part=GameObject.CreatePrimitive(shape);Destroy(part.GetComponent<Collider>());part.transform.SetParent(toy.root.transform,false);part.transform.localPosition=at;part.transform.localScale=scale;
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=color;material.SetFloat("_Smoothness",.35f);materials.Add(material);part.GetComponent<Renderer>().sharedMaterial=material;
            toy.pieces.Add(part.transform);return part.transform;
        }
        // Bounds follow the visible pieces so the room model, vision and pathing see where the toy really is.
        void Sync(Toy toy)
        {
            if(!toy.root)return;var renderers=toy.root.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            b.Expand(new Vector3(Mathf.Max(0,.3f-b.size.x),0,Mathf.Max(0,.3f-b.size.z)));toy.item.bounds=b;
        }
        void RebuildGrid(){if(brain&&brain.world!=null)brain.world.grid=new NavGrid(brain.world.floor,brain.world.objects.Where(o=>o.obstacle).Select(o=>o.bounds));}
        public Toy Find(string id,ToyKind? kind=null)
        {
            var exact=toys.FirstOrDefault(t=>t.item.id==id);if(exact!=null)return exact;
            var wanted=KindOf(id)??kind;if(!wanted.HasValue)return null;
            return toys.Where(t=>t.kind==wanted.Value).OrderBy(t=>t==selected?0:1).ThenBy(t=>Distance(t.Position,brain.transform.position)).FirstOrDefault();
        }
        public Toy Carried => toys.FirstOrDefault(t=>t.carried);
        // A free cell next to the toy, as close as possible to where the character should stand (e.g. behind a ball to push it).
        public bool StandSpot(Toy toy,Vector3 preferred,out Vector3 spot)
        {
            spot=default;float best=float.MaxValue;bool found=false;
            foreach(var cell in brain.world.grid.FreeCells())
            {
                float edge=SceneAwareness.FlatDistance(toy.item,cell);if(edge<.25f||edge>.85f||Distance(cell,brain.user.position)<.5f)continue;
                float score=Distance(cell,preferred)*2+Distance(cell,brain.transform.position)*.2f;
                if(score<best){best=score;spot=cell;found=true;}
            }
            return found;
        }
        public void Push(Toy toy,Vector3 velocity)
        {
            if(toy==null||toy.kind!=ToyKind.Ball||toy.carried)return;
            toy.velocity=velocity;
            // A rolling ball is not an obstacle for paths; it becomes one again where it stops.
            if(!toy.rolling){toy.rolling=true;toy.item.obstacle=false;RebuildGrid();}
        }
        // Launch speed that makes the ball come to rest after about this distance.
        public static float SpeedFor(float distance)=>Mathf.Sqrt(2*Deceleration*Mathf.Max(.2f,distance));
        // One physics step for a ball: gravity and floor bounces, rolling friction, walls and obstacle bounds. Returns false at rest.
        public static bool StepBall(ref Vector3 position,ref Vector3 velocity,float dt,Bounds floor,IList<Bounds> obstacles,float radius=BallRadius)
        {
            float ground=floor.max.y+radius;bool airborne=position.y>ground+.002f||velocity.y>0;
            if(airborne)velocity.y-=9.81f*dt;
            else
            {
                var flat=new Vector2(velocity.x,velocity.z);float speed=Mathf.Max(0,flat.magnitude-Deceleration*dt);
                flat=flat.sqrMagnitude>1e-8f?flat.normalized*speed:Vector2.zero;velocity=new Vector3(flat.x,0,flat.y);
            }
            float y=position.y+velocity.y*dt;
            if(y<ground){y=ground;velocity.y=velocity.y<-1?-velocity.y*.45f:0;}
            var next=new Vector3(position.x+velocity.x*dt,y,position.z);
            if(next.x<floor.min.x+radius||next.x>floor.max.x-radius||Blocked(next,obstacles,radius)){velocity.x=-velocity.x*.6f;next.x=position.x;}
            next.z=position.z+velocity.z*dt;
            if(next.z<floor.min.z+radius||next.z>floor.max.z-radius||Blocked(next,obstacles,radius)){velocity.z=-velocity.z*.6f;next.z=position.z;}
            position=next;
            return airborne||new Vector2(velocity.x,velocity.z).magnitude>.03f;
        }
        static bool Blocked(Vector3 p,IList<Bounds> obstacles,float radius)
        {
            for(int i=0;i<obstacles.Count;i++)
            {
                var b=obstacles[i];if(p.y-radius>b.max.y)continue;
                if(p.x>b.min.x-radius&&p.x<b.max.x+radius&&p.z>b.min.z-radius&&p.z<b.max.z+radius)return true;
            }
            return false;
        }
        void Update()
        {
            if(!brain||brain.world==null)return;float dt=Mathf.Min(Time.deltaTime,.05f);
            foreach(var toy in toys)
            {
                if(toy.kind==ToyKind.Spinner&&toy.spin>0&&toy.pieces.Count>1){toy.pieces[1].Rotate(0,toy.spin*dt,0,Space.Self);toy.spin=Mathf.MoveTowards(toy.spin,0,160*dt);}
                if(!toy.rolling)continue;
                var ball=toy.pieces[0];Vector3 center=ball.position,before=center;
                // The character's and user's legs stop the ball as well as furniture.
                var obstacles=brain.world.objects.Where(o=>o.obstacle&&o!=toy.item).Select(o=>o.bounds).ToList();
                obstacles.Add(new Bounds(Flat(brain.transform.position)+Vector3.up*.5f,new Vector3(.4f,1,.4f)));obstacles.Add(new Bounds(Flat(brain.user.position)+Vector3.up*.5f,new Vector3(.35f,1,.35f)));
                foreach(var other in toys)if(other!=toy&&!other.carried)obstacles.Add(other.item.bounds);
                bool moving=StepBall(ref center,ref toy.velocity,dt,brain.world.floor,obstacles);
                toy.root.transform.position+=center-before;
                var travel=Flat(center-before);if(travel.sqrMagnitude>1e-8f)ball.Rotate(Vector3.Cross(Vector3.up,travel.normalized),travel.magnitude/BallRadius*Mathf.Rad2Deg,Space.World);
                Sync(toy);
                if(!moving){toy.rolling=false;toy.velocity=Vector3.zero;toy.item.obstacle=true;RebuildGrid();}
            }
        }
        void LateUpdate()
        {
            var carried=Carried;if(carried==null||!brain)return;
            // Held in front of the chest; the ball's root sits below its centre.
            var holder=brain.transform;var at=holder.position+holder.forward*.33f+Vector3.up*(carried.kind==ToyKind.Ball?.42f:.5f);
            carried.root.transform.position=at;carried.root.transform.rotation=holder.rotation;Sync(carried);
        }
        public void PickUp(Toy toy)
        {
            if(toy==null||toy.carried)return;if(Carried!=null)PutDown(Carried,brain.transform.position+brain.transform.forward*.45f);
            toy.rolling=false;toy.velocity=Vector3.zero;toy.carried=true;toy.stacked=false;brain.world.objects.Remove(toy.item);RebuildGrid();
            if(toy.kind==ToyKind.Blocks)for(int i=0;i<toy.pieces.Count;i++){toy.pieces[i].localPosition=new Vector3(0,BlockSize/2+i*BlockSize*.95f,0);toy.pieces[i].localRotation=Quaternion.identity;}
        }
        public void PutDown(Toy toy,Vector3 point)
        {
            if(toy==null||!toy.carried)return;
            point=brain.world.grid.Snap(point);point.y=brain.world.floor.max.y;
            toy.carried=false;toy.root.transform.position=point;toy.root.transform.rotation=Quaternion.identity;
            if(toy.kind==ToyKind.Blocks)toy.stacked=true;
            Sync(toy);if(!brain.world.objects.Contains(toy.item))brain.world.objects.Add(toy.item);RebuildGrid();
        }
        // Moves each block along a small arc to its place; used for stacking and lining up.
        IEnumerator Arrange(Toy toy,Func<int,Vector3> place,int count,float each)
        {
            var order=toy.pieces.OrderBy(p=>Vector3.Distance(p.localPosition,place(0))).ToList();
            for(int i=0;i<order.Count;i++)
            {
                var piece=order[i];Vector3 from=piece.localPosition,to=i<count?place(i):new Vector3(piece.localPosition.x,BlockSize/2,piece.localPosition.z);Quaternion turn=piece.localRotation;
                for(float t=0;t<1;t+=Time.deltaTime/each)
                {
                    float s=Mathf.SmoothStep(0,1,t);piece.localPosition=Vector3.Lerp(from,to,s)+Vector3.up*Mathf.Sin(s*Mathf.PI)*.18f;piece.localRotation=Quaternion.Slerp(turn,Quaternion.identity,s);yield return null;
                }
                piece.localPosition=to;piece.localRotation=Quaternion.identity;Sync(toy);
            }
            RebuildGrid();
        }
        public IEnumerator Stack(Toy toy,int count,float each=.45f)
        {
            if(toy==null||toy.kind!=ToyKind.Blocks)yield break;
            count=Mathf.Clamp(count<=0?Blocks:count,2,Blocks);toy.stacked=true;
            yield return Arrange(toy,i=>new Vector3(0,BlockSize/2+i*BlockSize,0),count,each);
        }
        public IEnumerator LineUp(Toy toy,Vector3 axis,float each=.4f)
        {
            if(toy==null||toy.kind!=ToyKind.Blocks)yield break;
            toy.stacked=false;var local=toy.root.transform.InverseTransformDirection(Flat(axis).normalized);if(local.sqrMagnitude<.01f)local=Vector3.right;
            yield return Arrange(toy,i=>local*((i-(Blocks-1)/2f)*.2f)+Vector3.up*BlockSize/2,Blocks,each);
        }
        // Blocks fly off with a push and come to rest flat on the floor.
        public void KnockDown(Toy toy,Vector3 push){if(toy!=null&&toy.kind==ToyKind.Blocks&&!toy.carried){toy.stacked=false;StartCoroutine(Scatter(toy,Flat(push).normalized));}}
        IEnumerator Scatter(Toy toy,Vector3 push)
        {
            var random=new System.Random(serial*31+toy.pieces.Count);
            var velocity=toy.pieces.Select((p,i)=>push*(1.1f+(float)random.NextDouble())+Quaternion.Euler(0,random.Next(-60,60),0)*push*.5f+Vector3.up*(.6f+i*.25f)).ToArray();
            var spin=toy.pieces.Select(_=>new Vector3(random.Next(-400,400),random.Next(-400,400),random.Next(-400,400))).ToArray();
            for(float t=0;t<1.6f;t+=Time.deltaTime)
            {
                bool any=false;
                for(int i=0;i<toy.pieces.Count;i++)
                {
                    var piece=toy.pieces[i];if(velocity[i]==Vector3.zero)continue;any=true;
                    velocity[i]+=Vector3.down*9.81f*Time.deltaTime;var p=piece.position+velocity[i]*Time.deltaTime;float ground=brain.world.floor.max.y+BlockSize/2;
                    var room=brain.world.floor;p.x=Mathf.Clamp(p.x,room.min.x+.1f,room.max.x-.1f);p.z=Mathf.Clamp(p.z,room.min.z+.1f,room.max.z-.1f);
                    piece.Rotate(spin[i]*Time.deltaTime,Space.World);
                    if(p.y<=ground){p.y=ground;velocity[i]=Vector3.zero;piece.rotation=Quaternion.Euler(0,piece.eulerAngles.y,0);}
                    piece.position=p;
                }
                Sync(toy);if(!any)break;yield return null;
            }
            foreach(var piece in toy.pieces){var p=piece.position;p.y=brain.world.floor.max.y+BlockSize/2;piece.position=p;piece.rotation=Quaternion.Euler(0,piece.eulerAngles.y,0);}
            Sync(toy);RebuildGrid();
        }
        public void Spin(Toy toy,float degreesPerSecond){if(toy!=null&&toy.kind==ToyKind.Spinner)toy.spin=Mathf.Max(toy.spin,degreesPerSecond);}
        public IEnumerator Bounce(Toy toy,int times)
        {
            if(toy==null||toy.kind!=ToyKind.Ball)yield break;var ball=toy.pieces[0];var rest=new Vector3(0,BallRadius,0);
            for(float t=0;t<Mathf.Max(1,times)*.5f;t+=Time.deltaTime){ball.localPosition=rest+Vector3.up*Mathf.Abs(Mathf.Sin(t*Mathf.PI*2))*.4f;Sync(toy);yield return null;}
            ball.localPosition=rest;Sync(toy);
        }
        // Blocks interrupted mid-air drop straight down; nothing is left floating.
        public void Settle()
        {
            foreach(var toy in toys)
            {
                if(toy.kind!=ToyKind.Blocks||toy.carried)continue;
                foreach(var piece in toy.pieces)
                {
                    var p=piece.localPosition;float level=(p.y-BlockSize/2)/BlockSize;bool onTower=new Vector2(p.x,p.z).magnitude<.02f&&Mathf.Abs(level-Mathf.Round(level))<.05f;
                    if(!onTower){p.y=BlockSize/2;piece.localPosition=p;piece.localRotation=Quaternion.Euler(0,piece.localEulerAngles.y,0);}
                }
                Sync(toy);
            }
        }
        public void Next(){if(toys.Count>0)selected=toys[(toys.IndexOf(selected)+1)%toys.Count];Status?.Invoke("選択: "+SelectedName);}
        // The "play" button: a sensible default game with the selected toy, run as a motion plan.
        public bool PlaySelected()
        {
            Cancel();if(selected==null||!brain||!brain.performer){Status?.Invoke("まずおもちゃを置いてください。");return false;}
            var step=new MotionStep{target=selected.item.id,direction=MotionDirection.to_user,emotion=Emotion.happy};
            step.action=selected.kind==ToyKind.Ball?MotionAction.roll:selected.kind==ToyKind.Blocks?(selected.stacked?MotionAction.knock_down:MotionAction.stack):MotionAction.spin_toy;
            brain.performer.Run(new MotionPlan{steps=new[]{step},summary=selected.item.id+"で遊ぶ"},"");
            Status?.Invoke(selected.item.id+"で遊んでいます…");return brain.performer.Busy;
        }
        public void Cancel()
        {
            placing=null;if(brain&&brain.performer)brain.performer.Stop();
        }
        public void ClearToys()
        {
            Cancel();StopAllCoroutines();foreach(var toy in toys){if(brain&&brain.world!=null)brain.world.objects.Remove(toy.item);if(toy.root)Destroy(toy.root);}
            toys.Clear();selected=null;foreach(var material in materials)if(material)Destroy(material);materials.Clear();
            RebuildGrid();Status?.Invoke("おもちゃを片づけました。");
        }
        void OnApplicationPause(bool paused){if(paused)Cancel();}
        void OnApplicationFocus(bool focused){if(!focused)Cancel();}
        void OnDestroy(){Status=null;Noticed=null;ClearToys();}
    }
}
