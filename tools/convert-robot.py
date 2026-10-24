# Blender: converts the CC0 RobotExpressive glTF (three.js examples) to an FBX with every animation as a take.
# blender -b --factory-startup -P tools/convert-robot.py -- <input.glb> <output.fbx>
import bpy, sys, os
args = sys.argv[sys.argv.index('--') + 1:]
source, target = os.path.abspath(args[0]), os.path.abspath(args[1])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
for o in bpy.context.scene.objects:
    keys = [k.name for k in o.data.shape_keys.key_blocks] if o.type == 'MESH' and o.data.shape_keys else []
    print('ROBOT_OBJECT', o.type, o.name, keys)
for a in bpy.data.actions:
    print('ROBOT_ACTION', a.name, tuple(round(f) for f in a.frame_range))
# The loose Icosphere is not part of the robot (three.js never shows it) and would hide the legs.
for o in [o for o in bpy.context.scene.objects if o.name.startswith('Icosphere')]:
    bpy.data.objects.remove(o, do_unlink=True)
# Morph targets are driven at runtime; exporting their (static) shape-key animation makes duplicate takes
# that shadow the skeleton animation of the same name.
for key in bpy.data.shape_keys:
    key.animation_data_clear()
# FBX places bone-parented objects relative to the wrong end of the bone, which sinks the legs into the torso.
# Rebind every rigid part as a mesh skinned 100% to its bone, measured in the rest pose.
armature = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
armature.data.pose_position = 'REST'; bpy.context.view_layer.update()
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.parent_type == 'BONE']:
    bone, world = o.parent_bone, o.matrix_world.copy()
    o.parent = armature; o.parent_type = 'OBJECT'; o.matrix_world = world
    group = o.vertex_groups.new(name=bone); group.add(range(len(o.data.vertices)), 1.0, 'REPLACE')
    o.modifiers.new('Armature', 'ARMATURE').object = armature
    print('ROBOT_REBOUND', o.name, bone)
armature.data.pose_position = 'POSE'; bpy.context.view_layer.update()
os.makedirs(os.path.dirname(target), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=target, use_selection=False, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         use_mesh_modifiers=False, mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_ALL', path_mode='COPY', embed_textures=False)
print('ROBOT_EXPORTED', target, os.path.getsize(target))
