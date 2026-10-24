using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Toys driven by Unity physics: the ball rolls and bounces, blocks stack and topple, the top spins.
    // The character picks them up, carries, throws and places them; the user can push them with the pointer.
    // Sequencing (walking over, timing a kick) is MotionPerformer's job; the conversation stays connected throughout.
    public sealed class CompanionPlayground : MonoBehaviour
    {
        public enum ToyKind { Ball, Blocks, Spinner }
        public sealed class Toy
        {
            public GameObject root; public SceneObject item; public ToyKind kind; public bool stacked;
            public readonly List<Rigidbody> bodies = new List<Rigidbody>();
            public IEnumerable<Transform> pieces => bodies.Select(b => b.transform);
            public Vector3 Center => item.bounds.center;
            public bool carried => bodies.Any(b => b.isKinematic);
            public bool Moving => bodies.Any(b => !b.isKinematic && (b.linearVelocity.sqrMagnitude > .0025f || b.angularVelocity.sqrMagnitude > .25f));
            public bool rolling => Moving;
        }
        public const float BallRadius = .2f, Deceleration = .9f, BlockSize = .15f;
        public const int Blocks = 5, MaxToys = 8;
        readonly List<Toy> toys = new List<Toy>();
        readonly List<Material> materials = new List<Material>();
        // Bodies held by the character, each kept at an offset from the hands.
        readonly Dictionary<Rigidbody, Vector3> held = new Dictionary<Rigidbody, Vector3>();
        CharacterBrain brain; RoomPhysics room;
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
        float Floor => brain.world.floor.max.y;
        static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        public static string Label(ToyKind kind) => kind == ToyKind.Ball ? "ボール" : kind == ToyKind.Blocks ? "積み木" : "回転おもちゃ";
        public static ToyKind? KindOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Contains("ボール") || text.Contains("ぼーる") || text.Contains("ball") || text.Contains("Ball") || text.Contains("球")) return ToyKind.Ball;
            if (text.Contains("積み木") || text.Contains("積木") || text.Contains("つみき") || text.Contains("ブロック") || text.Contains("block")) return ToyKind.Blocks;
            if (text.Contains("回転") || text.Contains("コマ") || text.Contains("こま") || text.Contains("独楽") || text.Contains("spin")) return ToyKind.Spinner;
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
            placing=kind; Status?.Invoke("床をクリック／レーザーで選択して配置。最大8個。置いたおもちゃをクリックすると自分で動かせます。");
        }
        // A click on a toy pushes it (the user's own hand); a click on the floor places the chosen toy.
        public void PlaceFromRay(Ray ray)
        {
            if(!brain||brain.world==null)return;
            if(Touch(ray))return;
            if(!placing.HasValue)return;
            var plane=new Plane(Vector3.up,new Vector3(0,Floor,0));
            if(!plane.Raycast(ray,out float d)||d>8)return;
            // Do not place through furniture between the pointer and floor.
            if(Physics.Raycast(ray,out var hit,d,~0,QueryTriggerInteraction.Ignore)&&hit.point.y>Floor+.08f&&!hit.rigidbody){Status?.Invoke("家具の手前の空いている床を選んでください。");return;}
            TryPlace(placing.Value,ray.GetPoint(d));
        }
        public Toy Hit(Ray ray)
        {
            Toy best=null;float nearest=8;
            foreach(var toy in toys)foreach(var body in toy.bodies)foreach(var shape in body.GetComponentsInChildren<Collider>()){var b=shape.bounds;b.Expand(.08f);if(b.IntersectRay(ray,out float d)&&d<nearest){nearest=d;best=toy;}}
            return best;
        }
        public bool Touch(Ray ray)
        {
            var toy=Hit(ray);if(toy==null||toy.carried)return false;
            var push=Flat(ray.direction).normalized;string done;
            switch(toy.kind)
            {
                case ToyKind.Ball:Push(toy,push*2.4f);done="ユーザーが"+toy.item.id+"を転がしました。";break;
                case ToyKind.Blocks:Shove(toy,push,2.2f);done="ユーザーが"+toy.item.id+"を押しました。";break;
                default:Spin(toy,30);done="ユーザーが"+toy.item.id+"を回しました。";break;
            }
            selected=toy;Status?.Invoke(done.Replace("ユーザーが","あなたが"));Noticed?.Invoke(done);return true;
        }
        public bool TryPlace(ToyKind kind,Vector3 point)
        {
            if(!brain||brain.world==null||toys.Count>=MaxToys||
                !CanPlace(brain.world,point,brain.transform.position,brain.user.position,toys.Select(t=>t.Center)))
            {Status?.Invoke("ここには置けません。キャラクター・家具から離れた床を選んでください。");return false;}
            if(!room)room=RoomPhysics.Build(brain);
            point.y=Floor;
            string label=Label(kind);
            var root=new GameObject(label);root.transform.position=point;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,gameObject.scene);
            // Toys never block paths: the character's body nudges them aside physically instead.
            var toy=new Toy{root=root,kind=kind,item=new SceneObject{id=label+(++serial),label=label,obstacle=false}};
            if(kind==ToyKind.Ball){var ball=Piece(toy,PrimitiveType.Sphere,point+Vector3.up*BallRadius,Quaternion.identity,Vector3.one*BallRadius*2,new Color(.95f,.37f,.18f),.35f,RoomPhysics.Bouncy);ball.angularDamping=.15f;ball.maxAngularVelocity=60;}
            else if(kind==ToyKind.Blocks)
            {
                // Scattered loosely on the floor so there is something to stack.
                for(int i=0;i<Blocks;i++)
                {
                    float angle=i*72+serial*17;var at=point+Quaternion.Euler(0,angle,0)*Vector3.forward*(i==0?0:.24f)+Vector3.up*BlockSize/2;
                    var block=Piece(toy,PrimitiveType.Cube,at,Quaternion.Euler(0,angle*1.7f,0),Vector3.one*BlockSize,Color.HSVToRGB(i/(float)Blocks*.8f+.05f,.6f,.95f),.12f,RoomPhysics.Grippy);block.solverIterations=24;block.sleepThreshold=.02f;
                }
            }
            else
            {
                // A disc with a bar on top, kept upright while it spins.
                var top=new GameObject("Top");top.transform.SetParent(root.transform,false);top.transform.position=point+Vector3.up*.06f;
                Visual(toy,top.transform,PrimitiveType.Cylinder,Vector3.zero,new Vector3(.4f,.05f,.4f),new Color(.4f,.25f,.65f));Visual(toy,top.transform,PrimitiveType.Cube,new Vector3(0,.12f,0),new Vector3(.4f,.08f,.12f),new Color(1,.72f,.22f));
                var body=top.AddComponent<Rigidbody>();body.mass=.4f;body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;body.angularDamping=.35f;body.maxAngularVelocity=60;body.interpolation=RigidbodyInterpolation.Interpolate;toy.bodies.Add(body);
            }
            Sync(toy);toys.Add(toy);brain.world.objects.Add(toy.item);selected=toy;placing=null;
            Status?.Invoke(toy.item.id+"を置きました。声や文字で遊び方を頼んでみよう。");return true;
        }
        // Puts a new toy on free floor in front of the character, preferring the space between it and the user.
        public Toy Spawn(ToyKind kind)
        {
            if(!brain||brain.world==null||toys.Count>=MaxToys)return null;
            Vector3 self=brain.transform.position,user=brain.user.position,ideal=self+brain.transform.forward*1.2f;
            ideal=Vector3.Lerp(ideal,(self+user)/2,.4f);
            foreach(var cell in brain.world.grid.FreeCells().OrderBy(c=>Distance(c,ideal)))
                if(CanPlace(brain.world,cell,self,user,toys.Select(t=>t.Center))&&TryPlace(kind,cell))return toys[toys.Count-1];
            return null;
        }
        Material Paint(Color color){var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=color;material.SetFloat("_Smoothness",.35f);materials.Add(material);return material;}
        Rigidbody Piece(Toy toy,PrimitiveType shape,Vector3 at,Quaternion rotation,Vector3 scale,Color color,float mass,PhysicsMaterial surface)
        {
            var part=GameObject.CreatePrimitive(shape);part.transform.SetParent(toy.root.transform,true);part.transform.SetPositionAndRotation(at,rotation);part.transform.localScale=scale;
            part.GetComponent<Renderer>().sharedMaterial=Paint(color);part.GetComponent<Collider>().sharedMaterial=surface;
            var body=part.AddComponent<Rigidbody>();body.mass=mass;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            toy.bodies.Add(body);return body;
        }
        void Visual(Toy toy,Transform parent,PrimitiveType shape,Vector3 at,Vector3 scale,Color color)
        {
            var part=GameObject.CreatePrimitive(shape);part.transform.SetParent(parent,false);part.transform.localPosition=at;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=Paint(color);
            if(shape==PrimitiveType.Cylinder){Destroy(part.GetComponent<Collider>());part.AddComponent<BoxCollider>().size=new Vector3(1,2,1);}
        }
        // Bounds follow the visible pieces so the room model, vision and planning see where the toy really is.
        void Sync(Toy toy)
        {
            if(!toy.root)return;var renderers=toy.root.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);toy.item.bounds=b;
            if(toy.kind==ToyKind.Blocks){float top=toy.bodies.Max(p=>p.position.y)-Floor;toy.stacked=top>BlockSize*2.4f&&!toy.carried;}
        }
        public Toy Find(string id,ToyKind? kind=null)
        {
            var exact=toys.FirstOrDefault(t=>t.item.id==id);if(exact!=null)return exact;
            var wanted=KindOf(id)??kind;if(!wanted.HasValue)return null;
            return toys.Where(t=>t.kind==wanted.Value).OrderBy(t=>t==selected?0:1).ThenBy(t=>Distance(t.Center,brain.transform.position)).FirstOrDefault();
        }
        public Toy Carried => toys.FirstOrDefault(t=>t.carried);
        // A free cell next to the toy, as close as possible to where the character should stand (e.g. behind a ball to push it).
        public bool StandSpot(Toy toy,Vector3 preferred,out Vector3 spot)
        {
            spot=default;float best=float.MaxValue;bool found=false;
            foreach(var cell in brain.world.grid.FreeCells())
            {
                float edge=SceneAwareness.FlatDistance(toy.item,cell);if(edge<.22f||edge>.8f||Distance(cell,brain.user.position)<.5f)continue;
                float score=Distance(cell,preferred)*2+Distance(cell,brain.transform.position)*.2f;
                if(score<best){best=score;spot=cell;found=true;}
            }
            return found;
        }
        // Where held things sit: between the hands, in front of the chest.
        public Vector3 Hands
        {
            get
            {
                var t=brain.transform;var rig=brain.body?brain.body.rig:null;
                if(rig&&rig.chest)return rig.chest.position+t.forward*.24f-Vector3.up*.08f;
                return t.position+t.forward*.3f+Vector3.up*.5f;
            }
        }
        public void Push(Toy toy,Vector3 velocity)
        {
            if(toy==null||toy.kind!=ToyKind.Ball||toy.carried)return;
            var ball=toy.bodies[0];ball.WakeUp();ball.linearVelocity=velocity;
            var flat=Flat(velocity);if(flat.sqrMagnitude>.0001f)ball.angularVelocity=Vector3.Cross(Vector3.up,flat)/BallRadius;
        }
        // Launch speed that makes the ball come to rest after about this distance.
        public static float SpeedFor(float distance)=>Mathf.Sqrt(2*Deceleration*Mathf.Max(.2f,distance));
        // Velocity that carries a thrown body from one point to another in the given time.
        public static Vector3 Ballistic(Vector3 from,Vector3 to,float time)
        {
            time=Mathf.Max(.15f,time);var delta=to-from;
            return new Vector3(delta.x/time,(delta.y+.5f*-Physics.gravity.y*time*time)/time,delta.z/time);
        }
        public void Shove(Toy toy,Vector3 direction,float strength)
        {
            if(toy==null||toy.carried)return;direction=Flat(direction).normalized;
            foreach(var body in toy.bodies){body.WakeUp();float height=Mathf.Clamp01((body.position.y-Floor)/.6f);body.AddForce((direction*(1+height)+Vector3.up*height*.6f)*strength,ForceMode.VelocityChange);body.AddTorque(UnityEngine.Random.insideUnitSphere*strength*4,ForceMode.VelocityChange);}
        }
        public void KnockDown(Toy toy,Vector3 push)=>Shove(toy,push,2.6f);
        public void Spin(Toy toy,float radiansPerSecond){if(toy!=null&&toy.kind==ToyKind.Spinner&&!toy.carried){var body=toy.bodies[0];body.WakeUp();body.angularVelocity=Vector3.up*radiansPerSecond;}}
        void FixedUpdate()
        {
            if(!brain||brain.world==null)return;
            // Rolling resistance: a steady slowdown while the ball is on the floor, so it comes to rest where intended.
            foreach(var toy in toys)
            {
                if(toy.kind!=ToyKind.Ball)continue;var ball=toy.bodies[0];if(ball.isKinematic||ball.position.y>Floor+BallRadius+.03f)continue;
                var v=ball.linearVelocity;var flat=Flat(v);float speed=flat.magnitude;if(speed<1e-4f)continue;
                float slower=Mathf.Max(0,speed-Deceleration*Time.fixedDeltaTime);ball.linearVelocity=flat/speed*slower+Vector3.up*v.y;
                ball.angularVelocity=Vector3.Cross(Vector3.up,flat/speed*slower)/BallRadius;
            }
        }
        void Update(){if(brain&&brain.world!=null)foreach(var toy in toys)if(toy.carried||toy.Moving||Time.frameCount%15==0)Sync(toy);}
        void LateUpdate()
        {
            if(held.Count==0||!brain)return;var hands=Hands;var rotation=Quaternion.Euler(0,brain.transform.eulerAngles.y,0);
            foreach(var pair in held){if(!pair.Key)continue;pair.Key.transform.SetPositionAndRotation(hands+rotation*pair.Value,rotation);}
        }
        // Holding: the bodies stop simulating and follow the hands. Blocks are held as a small stack, or one at a time.
        public List<Rigidbody> PickUp(Toy toy,bool single=false)
        {
            var taken=new List<Rigidbody>();if(toy==null)return taken;
            var bodies=single||toy.kind!=ToyKind.Blocks?toy.bodies.OrderBy(b=>Vector3.Distance(b.position,Hands)).Take(1):toy.bodies;
            int level=0;
            foreach(var body in bodies)
            {
                if(held.ContainsKey(body))continue;Hold(body,toy.kind==ToyKind.Blocks?Vector3.up*(level++*BlockSize*1.02f):Vector3.zero);taken.Add(body);
            }
            if(brain.body)brain.body.Holding=held.Count>0;Sync(toy);return taken;
        }
        void Hold(Rigidbody body,Vector3 offset){body.isKinematic=true;body.detectCollisions=false;held[body]=offset;}
        // Lets go of held bodies with a velocity (zero: they simply drop).
        public void Release(IEnumerable<Rigidbody> bodies,Vector3 velocity,Vector3 spin=default)
        {
            foreach(var body in bodies.ToList())
            {
                if(!body||!held.Remove(body))continue;
                body.isKinematic=false;body.detectCollisions=true;body.WakeUp();body.linearVelocity=velocity;body.angularVelocity=spin;
            }
            if(brain.body)brain.body.Holding=held.Count>0;
        }
        public void ReleaseAll(Vector3 velocity)=>Release(held.Keys,velocity);
        public void PutDown(Toy toy,Vector3 point)
        {
            if(toy==null||!toy.carried)return;
            point=brain.world.grid.Snap(point);var mine=toy.bodies.Where(b=>held.ContainsKey(b)).ToList();
            for(int i=0;i<mine.Count;i++){var size=toy.kind==ToyKind.Ball?BallRadius:toy.kind==ToyKind.Blocks?BlockSize/2:.06f;mine[i].transform.position=new Vector3(point.x,Floor+size+i*BlockSize*1.01f,point.z);}
            Release(mine,Vector3.zero);Sync(toy);
        }
        // Throws the held body so it lands on the target point.
        public void Throw(IEnumerable<Rigidbody> bodies,Vector3 target,float flight)
        {
            var list=bodies.ToList();if(list.Count==0)return;
            var from=list[0].position;Release(list,Ballistic(from,target,flight),UnityEngine.Random.insideUnitSphere*6);
        }
        // Lifts one body along a small arc to a resting place and lets physics take over (used to stack and line up blocks).
        public IEnumerator Place(Rigidbody body,Vector3 position,Quaternion rotation,float seconds=.5f)
        {
            if(!body)yield break;held.Remove(body);body.isKinematic=true;body.detectCollisions=false;
            Vector3 from=body.position;Quaternion turn=body.rotation;var via=Hands;
            for(float t=0;t<1;t+=Time.deltaTime/seconds)
            {
                float s=Mathf.SmoothStep(0,1,t);
                // Through the hands, so the block visibly passes through the character's grip.
                var a=Vector3.Lerp(from,via,s);var b=Vector3.Lerp(via,position+Vector3.up*.02f,s);body.transform.SetPositionAndRotation(Vector3.Lerp(a,b,s),Quaternion.Slerp(turn,rotation,s));
                yield return null;
            }
            body.transform.SetPositionAndRotation(position+Vector3.up*.004f,rotation);body.isKinematic=false;body.detectCollisions=true;body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
            if(brain.body)brain.body.Holding=held.Count>0;
        }
        // Builds a tower one block at a time; a block that slides off is simply tried again on top of what stands.
        public IEnumerator Stack(Toy toy,int count,float each=.5f)
        {
            if(toy==null||toy.kind!=ToyKind.Blocks)yield break;
            count=Mathf.Clamp(count<=0?Blocks:count,2,Blocks);
            Release(toy.bodies.Where(b=>held.ContainsKey(b)),Vector3.zero);
            var baseBlock=toy.bodies.OrderBy(b=>b.position.y).ThenBy(b=>Distance(b.position,toy.Center)).First();
            var spot=new Vector3(baseBlock.position.x,Floor+BlockSize/2,baseBlock.position.z);var yaw=Quaternion.Euler(0,brain.transform.eulerAngles.y,0);
            yield return Place(baseBlock,spot,yaw,each*.6f);yield return new WaitForSeconds(.15f);
            var tower=new List<Rigidbody>{baseBlock};
            for(int attempt=0;tower.Count<count&&attempt<count*2;attempt++)
            {
                var next=toy.bodies.Where(b=>!tower.Contains(b)).OrderBy(b=>Vector3.Distance(b.position,spot)).FirstOrDefault();if(!next)break;
                // Only blocks still standing on the column count as the tower.
                tower=tower.Where(b=>Distance(b.position,spot)<BlockSize*.5f).OrderBy(b=>b.position.y).ToList();
                float top=tower.Count==0?Floor:tower.Max(b=>b.position.y)+BlockSize/2;
                yield return Place(next,new Vector3(spot.x,top+BlockSize/2,spot.z),yaw,each);yield return new WaitForSeconds(.25f);
                if(Distance(next.position,spot)<BlockSize*.5f&&next.position.y>top)tower.Add(next);
            }
            Sync(toy);
        }
        public IEnumerator LineUp(Toy toy,Vector3 axis,float each=.45f)
        {
            if(toy==null||toy.kind!=ToyKind.Blocks)yield break;
            Release(toy.bodies.Where(b=>held.ContainsKey(b)),Vector3.zero);
            axis=Flat(axis).normalized;if(axis.sqrMagnitude<.01f)axis=Vector3.right;var center=Flat(toy.Center)+Vector3.up*(Floor+BlockSize/2);var yaw=Quaternion.LookRotation(Vector3.Cross(axis,Vector3.up));
            var order=toy.bodies.OrderBy(b=>Vector3.Dot(b.position,axis)).ToList();
            for(int i=0;i<order.Count;i++)yield return Place(order[i],center+axis*((i-(order.Count-1)/2f)*.22f),yaw,each);
            Sync(toy);
        }
        public IEnumerator Bounce(Toy toy,int times)
        {
            if(toy==null||toy.kind!=ToyKind.Ball)yield break;var ball=toy.bodies[0];
            for(int i=0;i<Mathf.Max(1,times);i++)
            {
                if(!held.ContainsKey(ball))PickUp(toy);
                Release(new[]{ball},Vector3.down*3.2f);
                // Catch it on the way back up.
                for(float t=0;t<1.2f;t+=Time.deltaTime){yield return null;if(t>.2f&&ball.linearVelocity.y<=0&&ball.position.y>Hands.y-.35f)break;}
                if(i<times-1)PickUp(toy);
            }
        }
        // Anything left in the air when play is interrupted falls under gravity.
        public void Settle(){ReleaseAll(Vector3.zero);}
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
            Cancel();StopAllCoroutines();held.Clear();if(brain&&brain.body)brain.body.Holding=false;
            foreach(var toy in toys){if(brain&&brain.world!=null)brain.world.objects.Remove(toy.item);if(toy.root)Destroy(toy.root);}
            toys.Clear();selected=null;foreach(var material in materials)if(material)Destroy(material);materials.Clear();
            Status?.Invoke("おもちゃを片づけました。");
        }
        void OnApplicationPause(bool paused){if(paused)Cancel();}
        void OnApplicationFocus(bool focused){if(!focused)Cancel();}
        void OnDestroy(){Status=null;Noticed=null;ClearToys();if(room)Destroy(room.gameObject);}
    }
}
