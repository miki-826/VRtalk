using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestAI
{
    // Invisible colliders for the room (floor, walls, furniture boxes) plus kinematic stand-ins for the character and the user,
    // so toys roll, bounce, stack and topple with Unity physics. Lives in the room scene and goes away with it.
    public sealed class RoomPhysics : MonoBehaviour
    {
        static PhysicsMaterial bouncy, grippy, soft;
        public static PhysicsMaterial Bouncy => bouncy ? bouncy : bouncy = new PhysicsMaterial("Toy ball") { bounciness = .62f, dynamicFriction = .45f, staticFriction = .5f, bounceCombine = PhysicsMaterialCombine.Maximum };
        public static PhysicsMaterial Grippy => grippy ? grippy : grippy = new PhysicsMaterial("Toy block") { bounciness = .05f, dynamicFriction = .7f, staticFriction = .9f, frictionCombine = PhysicsMaterialCombine.Maximum };
        public static PhysicsMaterial Soft => soft ? soft : soft = new PhysicsMaterial("Room") { bounciness = .2f, dynamicFriction = .5f, staticFriction = .6f };
        CharacterBrain brain; Rigidbody character, user; CapsuleCollider userShape; float floor;
        public static RoomPhysics Build(CharacterBrain brain)
        {
            var go = new GameObject("Room physics"); SceneManager.MoveGameObjectToScene(go, brain.gameObject.scene);
            var physics = go.AddComponent<RoomPhysics>(); physics.brain = brain; physics.Create(); return physics;
        }
        void Create()
        {
            var room = brain.world.floor; floor = room.max.y; const float height = 3, thick = .5f;
            Box("Floor", new Vector3(room.center.x, floor - thick / 2, room.center.z), new Vector3(room.size.x + 2, thick, room.size.z + 2));
            Box("Wall W", new Vector3(room.min.x - thick / 2, floor + height / 2, room.center.z), new Vector3(thick, height, room.size.z + 2));
            Box("Wall E", new Vector3(room.max.x + thick / 2, floor + height / 2, room.center.z), new Vector3(thick, height, room.size.z + 2));
            Box("Wall S", new Vector3(room.center.x, floor + height / 2, room.min.z - thick / 2), new Vector3(room.size.x + 2, height, thick));
            Box("Wall N", new Vector3(room.center.x, floor + height / 2, room.max.z + thick / 2), new Vector3(room.size.x + 2, height, thick));
            foreach (var o in brain.world.objects) if (o.obstacle && o.bounds.size.y > .12f) Box(o.id, o.bounds.center, o.bounds.size);
            character = Body("Character body", .2f, 1); user = Body("User body", .18f, 1.6f); userShape = user.GetComponent<CapsuleCollider>();
        }
        void Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.position = center;
            var box = go.AddComponent<BoxCollider>(); box.size = size; box.sharedMaterial = Soft;
        }
        Rigidbody Body(string name, float radius, float height)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var shape = go.AddComponent<CapsuleCollider>(); shape.radius = radius; shape.height = height; shape.sharedMaterial = Soft;
            var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate; return body;
        }
        void FixedUpdate()
        {
            if (!brain || brain.user == null) return;
            var self = brain.transform.position; character.MovePosition(new Vector3(self.x, floor + .5f, self.z));
            // The user's legs: from the floor up to just below the head.
            var head = brain.user.position; float tall = Mathf.Max(.6f, head.y - floor - .1f);
            userShape.height = tall; user.MovePosition(new Vector3(head.x, floor + tall / 2, head.z));
        }
    }
}
