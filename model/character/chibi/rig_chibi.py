import bpy, os, math, json
from mathutils import Vector, Matrix
BASE=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(BASE,'UnityReady');os.makedirs(OUT,exist_ok=True)
ASSET=os.path.join(OUT,'Assets','WhiteChibi');os.makedirs(ASSET,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(BASE,'white_chibi.blend'))
scene=bpy.context.scene
objects=[o for c in ('CHARACTER','FACE • hide for blank mannequin') for o in bpy.data.collections[c].objects]
head=next(o for o in objects if o.name.startswith('Head'))
# Bake the procedural cheek tint to a portable color texture.
bpy.ops.object.select_all(action='DESELECT');head.select_set(True);bpy.context.view_layer.objects.active=head
im=bpy.data.images.new('Chibi_Head_BaseColor',1024,1024,alpha=False)
nt=head.active_material.node_tree;tex=nt.nodes.new('ShaderNodeTexImage');tex.image=im;nt.nodes.active=tex
scene.render.engine='CYCLES';scene.cycles.samples=8
bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR'},margin=12)
im.filepath_raw=os.path.join(ASSET,'Chibi_Head_BaseColor.png');im.file_format='PNG';im.save()
nt.links.new(tex.outputs['Color'],nt.nodes.get('Principled BSDF').inputs['Base Color'])
head.active_material.name='Chibi_Head'
for o in objects:
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    if o.type=='CURVE':bpy.ops.object.convert(target='MESH')
    for mod in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    # Preserve smooth normals while keeping runtime mesh sizes moderate.
    if len(o.data.polygons)>1500:
        dec=o.modifiers.new('Runtime mesh','DECIMATE');dec.ratio=.42;bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    if o!=head:
        for m in o.data.materials:
            if m and m.name.startswith('Porcelain'):m.name='Chibi_White'

arm=bpy.data.armatures.new('WhiteChibi_Skeleton');rig=bpy.data.objects.new('WhiteChibi_Rig',arm);scene.collection.objects.link(rig);rig.show_in_front=True;arm.display_type='OCTAHEDRAL'
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
def bone(name,h,t,parent=None):
    b=arm.edit_bones.new(name);b.head=h;b.tail=t
    if parent:b.parent=arm.edit_bones[parent]
    return b
bone('Root',(0,0,0),(0,0,.2))
bone('Hips',(0,0,.74),(0,0,1.02),'Root')
bone('Spine',(0,0,1.02),(0,0,1.28),'Hips')
bone('Chest',(0,0,1.28),(0,0,1.55),'Spine')
bone('Neck',(0,0,1.55),(0,0,1.85),'Chest')
bone('Head',(0,0,1.85),(0,0,3.25),'Neck')
for s,side in [(1,'Left'),(-1,'Right')]:
    bone(side+'Shoulder',(s*.10,0,1.51),(s*.37,0,1.51),'Chest')
    bone(side+'UpperArm',(s*.37,0,1.51),(s*.57,0,1.22),side+'Shoulder')
    bone(side+'LowerArm',(s*.57,0,1.22),(s*.69,-.02,1.00),side+'UpperArm')
    bone(side+'Hand',(s*.69,-.02,1.00),(s*.72,-.03,.79),side+'LowerArm')
    bone(side+'UpperLeg',(s*.255,0,.79),(s*.255,-.018,.47),'Hips')
    bone(side+'LowerLeg',(s*.255,-.018,.47),(s*.27,0,.20),side+'UpperLeg')
    bone(side+'Foot',(s*.27,0,.20),(s*.27,-.23,.12),side+'LowerLeg')
    bone(side+'Toes',(s*.27,-.23,.12),(s*.27,-.39,.12),side+'Foot')
bpy.ops.object.mode_set(mode='OBJECT')

def smooth(a,b,v):
    t=max(0,min(1,(v-a)/(b-a)));return t*t*(3-2*t)
def skin(o,weights):
    for name in arm.bones.keys():o.vertex_groups.new(name=name)
    for v in o.data.vertices:
        w=weights(o.matrix_world@v.co);w={k:n for k,n in w.items() if n>1e-5};total=sum(w.values())
        for k,n in w.items():o.vertex_groups[k].add([v.index],n/total,'REPLACE')
    mod=o.modifiers.new('Armature','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False
    o.parent=rig
def body_weights(p):
    x,y,z=p;side='Left' if x>0 else 'Right';x=abs(x)
    # Anatomical masks prevent cross-binding of the short hands and hips.
    boundary=max(.28,.49*math.sqrt(max(0,1-((z-1.12)/.64)**2))); armw=smooth(boundary-.005,boundary+.085,x)*smooth(.50,.66,z)
    legw=1-smooth(.66,.91,z)
    foot=1-smooth(.19,.33,z);knee=smooth(.37,.57,z)
    hand=1-smooth(.97,1.11,z);elbow=smooth(1.14,1.34,z)
    spine=smooth(.96,1.18,z);chest=smooth(1.22,1.43,z);neck=smooth(1.55,1.76,z)
    center={'Hips':1-spine,'Spine':spine*(1-chest),'Chest':spine*chest*(1-neck),'Neck':spine*chest*neck}
    w={k:v*(1-armw)*(1-legw) for k,v in center.items()}
    w.update({side+'UpperLeg':legw*(1-armw)*(1-foot)*knee,side+'LowerLeg':legw*(1-armw)*(1-foot)*(1-knee),side+'Foot':legw*(1-armw)*foot,
              side+'UpperArm':armw*(1-hand)*elbow,side+'LowerArm':armw*(1-hand)*(1-elbow),side+'Hand':armw*hand})
    return w
body=next(o for o in objects if o.name.startswith('Body'))
for o in objects:
    if o!=body:skin(o,lambda p:{'Head':1})
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
body.modifiers[-1].name='Armature'
for v in body.data.vertices:
    values=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-5],key=lambda p:p[1],reverse=True)[:4]
    if not values:raise RuntimeError('Automatic weighting failed')
    total=sum(w for g,w in values)
    for group in body.vertex_groups:group.remove([v.index])
    for g,w in values:body.vertex_groups[g].add([v.index],w/total,'REPLACE')

# Rotate both arm chains into an explicit horizontal T-pose and rebind it.
for s,side in [(1,'Left'),(-1,'Right')]:
    for name in ('UpperArm','LowerArm','Hand'):
        pb=rig.pose.bones[side+name];b=arm.bones[side+name]
        direction=Vector((s,0,0));q=(b.tail_local-b.head_local).rotation_difference(direction)
        # Retain the already posed parent's joint location.
        bpy.context.view_layer.update();h=pb.head.copy()
        pb.matrix=Matrix.Translation(h)@q.to_matrix().to_4x4()@b.matrix_local.to_3x3().to_4x4()
        bpy.context.view_layer.update()
for o in objects:
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier='Armature')
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='POSE');bpy.ops.pose.armature_apply(selected=False);bpy.ops.object.mode_set(mode='OBJECT')
for o in objects:
    m=o.modifiers.new('Armature','ARMATURE');m.object=rig

# Scale to a practical 1.1 metre character, applying transforms consistently.
for o in objects:
    for v in o.data.vertices:v.co*=1.1/3.58
    o.location*=1.1/3.58
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for b in arm.edit_bones:b.head*=1.1/3.58;b.tail*=1.1/3.58
bpy.ops.object.mode_set(mode='OBJECT')
for o in objects:
    o.name=o.name.replace(' • ','_').replace(' ','_')
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=rig
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
bpy.ops.export_scene.fbx(filepath=os.path.join(ASSET,'WhiteChibi.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,path_mode='COPY',embed_textures=False)
im.filepath='//UnityReady/Assets/WhiteChibi/Chibi_Head_BaseColor.png'
im.pack()
cam=scene.camera;cam.location=(1.8,-3.4,1.5);cam.rotation_euler=(Vector((0,0,.56))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=1.7
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':area.spaces.active.region_3d.view_distance=2;area.spaces.active.region_3d.view_location=(0,0,.55)
scene.render.resolution_x=900;scene.render.resolution_y=900;scene.cycles.samples=24
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BASE,'white_chibi_rigged.blend'))
# Exercise joints for a separate diagnostic preview; do not export this pose.
for side in ('Left','Right'):
    pb=rig.pose.bones[side+'UpperArm'];pb.rotation_mode='XYZ';pb.rotation_euler[0]=math.radians(45 if side=='Left' else -60)
rig.pose.bones['LeftLowerArm'].rotation_mode='XYZ';rig.pose.bones['LeftLowerArm'].rotation_euler[2]=math.radians(-65)
rig.pose.bones['LeftUpperLeg'].rotation_mode='XYZ';rig.pose.bones['LeftUpperLeg'].rotation_euler[0]=math.radians(-20)
rig.pose.bones['LeftLowerLeg'].rotation_mode='XYZ';rig.pose.bones['LeftLowerLeg'].rotation_euler[0]=math.radians(35)
scene.render.filepath=os.path.join(BASE,'rig_pose_check.png');bpy.ops.render.render(write_still=True)
report={'bone_count':len(arm.bones),'mesh_count':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),'unweighted_vertices':sum(1 for o in objects for v in o.data.vertices if not v.groups),'max_influences':max(len(v.groups) for o in objects for v in o.data.vertices),'height_m':1.1}
assert report['unweighted_vertices']==0
assert report['max_influences']<=4
with open(os.path.join(BASE,'rig_validation.json'),'w') as f:json.dump(report,f,indent=2)
print('RIG_COMPLETE',report)


