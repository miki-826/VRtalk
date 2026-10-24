import bpy, math, os, json
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name != 'Collection': bpy.data.collections.remove(c)
model = bpy.data.collections.get('Collection'); model.name = 'CHARACTER'
face = bpy.data.collections.new('FACE • hide for blank mannequin'); bpy.context.scene.collection.children.link(face)
studio = bpy.data.collections.new('STUDIO'); bpy.context.scene.collection.children.link(studio)

def move(obj, col):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    col.objects.link(obj)
def mat(name, color, rough=.4):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    m.node_tree.nodes.clear(); bs=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled'); bs.name='Principled BSDF'; output=m.node_tree.nodes.new('ShaderNodeOutputMaterial'); m.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface']); bs.inputs['Base Color'].default_value=(*color,1); bs.inputs['Roughness'].default_value=rough
    return m
white=mat('Porcelain • warm white',(.88,.855,.825),.3)
white.node_tree.nodes.get('Principled BSDF').inputs['Subsurface Weight'].default_value=.055
dark=mat('Eyes • cocoa',(.038,.024,.021),.22)
brow=mat('Brows',(.16,.115,.095),.5)
mouth=mat('Smile • inner',(.24,.065,.055),.5)
tongue=mat('Smile • pink',(.70,.24,.22),.43)
shine=mat('Eye highlights',(.98,.98,.98),.2)
pink=mat('Cheeks • soft peach',(.89,.54,.50),.65)

def uv(name, loc, scale, material=white, col=model, shape=1):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=48, location=loc)
    o=bpy.context.object; o.name=name
    if shape!=1:
        for v in o.data.vertices:
            for axis in (0,2):
                a=v.co[axis]; v.co[axis]=math.copysign(abs(a)**shape,a)
    o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(material)
    for p in o.data.polygons:p.use_smooth=True
    move(o,col); return o

parts=[]
parts.append(uv('Torso', (0,0,1.12),(.49,.32,.64)))
parts.append(uv('Neck',(0,0,1.73),(.22,.21,.27)))
for s,side in [(-1,'L'),(1,'R')]:
    parts.append(uv('Leg.'+side,(s*.255,.005,.47),(.235,.24,.43)))
    parts.append(uv('Foot.'+side,(s*.27,-.115,.16),(.265,.35,.17)))
    arm=uv('Arm.'+side,(s*.53,0,1.23),(.185,.185,.43)); arm.rotation_euler[1]=-s*.43; parts.append(arm)
    parts.append(uv('Palm.'+side,(s*.70,-.025,.925),(.18,.19,.225)))
    parts.append(uv('Thumb.'+side,(s*.565,-.15,.95),(.09,.105,.14)))
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
body=bpy.context.object; body.name='Body • seamless rounded mannequin'
rem=body.modifiers.new('Union sculpt volumes','REMESH');rem.mode='VOXEL';rem.voxel_size=.025
bpy.ops.object.modifier_apply(modifier=rem.name)
sm=body.modifiers.new('Relax surface','SMOOTH');sm.factor=1.25;sm.iterations=8
bpy.ops.object.modifier_apply(modifier=sm.name)
sub=body.modifiers.new('Silky surface','SUBSURF');sub.levels=1;sub.render_levels=1
for p in body.data.polygons:p.use_smooth=True
head=uv('Head • oversized rounded silhouette',(0,0,2.60),(.99,.765,.98),shape=.88)
for s,side in [(-1,'L'),(1,'R')]:uv('Ear.'+side,(s*.965,-.005,2.36),(.17,.17,.255))

def surface(x,z):
    nx=abs(x/.99)**(1/.88); nz=abs((z-2.60)/.98)**(1/.88)
    return -.765*math.sqrt(max(.01,1-nx*nx-nz*nz))
for s,side in [(-1,'L'),(1,'R')]:
    x=s*.39;z=2.55;y=surface(x,z)
    eye=uv('Eye.'+side,(x,y-.014,z),(.119,.060,.193),dark,face)
    eye.rotation_euler[2]=s*.20
    uv('Sparkle.'+side,(x-.031,y-.075,z+.078),(.033,.016,.046),shine,face)
    # Small broad, softly colored cheek disks conform to the head surface.
    verts=[]; faces=[]; rings=12; seg=64
    for r in range(rings+1):
        rr=max(.0001,r/rings)
        for k in range(seg):
            a=2*math.pi*k/seg; xx=s*.58+.16*rr*math.cos(a); zz=2.25+.09*rr*math.sin(a)
            verts.append((xx,surface(xx,zz)-.003,zz))
    for r in range(rings):
        for k in range(seg):faces.append((r*seg+k,r*seg+(k+1)%seg,(r+1)*seg+(k+1)%seg,(r+1)*seg+k))
    me=bpy.data.meshes.new('Cheek');me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new('Blush.'+side,me);face.objects.link(ob)
    for r in range(rings):
        t=(1-(r+.5)/rings)**2*.55
        m=mat('Blush falloff '+side+str(r),tuple(white.diffuse_color[i]*(1-t)+pink.diffuse_color[i]*t for i in range(3)),.4);me.materials.append(m)
    for p in me.polygons:p.material_index=p.index//seg;p.use_smooth=True

def curve(name,points,radius,material):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.bevel_depth=radius;cu.bevel_resolution=4
    sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
    for p,co in zip(sp.points,points):p.co=(*co,1)
    ob=bpy.data.objects.new(name,cu);face.objects.link(ob);cu.materials.append(material)
for s,side in [(-1,'L'),(1,'R')]:
    points=[]
    for i in range(25):
        t=i/24; x=s*.39+(t-.5)*.19; z=2.90+.035*math.sin(math.pi*t)
        points.append((x,surface(x,z)-.014,z))
    curve('Brow.'+side,points,.018,brow)

def patch(name, coords, material, offset):
    verts=[(x,surface(x,z)-offset,z) for x,z in coords]
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],[tuple(range(len(verts)))]);me.update()
    ob=bpy.data.objects.new(name,me);face.objects.link(ob);me.materials.append(material)
    sol=ob.modifiers.new('Tiny depth','SOLIDIFY');sol.thickness=.008
    bevel=ob.modifiers.new('Soft edge','BEVEL');bevel.width=.009;bevel.segments=3
    return ob
coords=[]
for i in range(33):
    x=-.17+.34*i/32;z=2.265-.025*(1-(x/.17)**2);coords.append((x,z))
for i in range(33):
    x=.17-.34*i/32;z=2.265-.16*math.sqrt(max(0,1-(x/.17)**2));coords.append((x,z))
patch('Smile',coords,mouth,.018)
coords=[]
for i in range(25):
    x=-.115+.23*i/24;coords.append((x,2.14+.038*math.sqrt(max(0,1-(x/.115)**2))))
for i in range(25):
    x=.115-.23*i/24;coords.append((x,2.137-.027*math.sqrt(max(0,1-(x/.115)**2))))
patch('Smile • tongue',coords,tongue,.025)

# Blend cheek color directly into porcelain, avoiding raised cheek geometry.
for ob in list(face.objects):
    if ob.name.startswith('Blush.'): bpy.data.objects.remove(ob,do_unlink=True)
hm=white.copy();hm.name='Porcelain with soft blush';head.data.materials.clear();head.data.materials.append(hm)
nt=hm.node_tree;bs=nt.nodes.get('Principled BSDF');tc=nt.nodes.new('ShaderNodeTexCoord')
base=nt.nodes.new('ShaderNodeRGB');base.outputs[0].default_value=(.88,.855,.825,1)
color=base.outputs[0]
for s in [-1,1]:
    dist=nt.nodes.new('ShaderNodeVectorMath');dist.operation='DISTANCE';dist.inputs[1].default_value=(s*.58,surface(s*.58,2.25),-.35);nt.links.new(tc.outputs['Object'],dist.inputs[0])
    ramp=nt.nodes.new('ShaderNodeMapRange');ramp.clamp=True;ramp.interpolation_type='SMOOTHSTEP';ramp.inputs['From Min'].default_value=.025;ramp.inputs['From Max'].default_value=.19;ramp.inputs['To Min'].default_value=.55;ramp.inputs['To Max'].default_value=0;nt.links.new(dist.outputs['Value'],ramp.inputs['Value'])
    mix=nt.nodes.new('ShaderNodeMixRGB');mix.inputs[2].default_value=(.89,.39,.36,1);nt.links.new(ramp.outputs[0],mix.inputs[0]);nt.links.new(color,mix.inputs[1]);color=mix.outputs[0]
nt.links.new(color,bs.inputs['Base Color'])
floor=mat('Backdrop',(.78,.81,.84),.8)
bpy.ops.mesh.primitive_plane_add(size=200);o=bpy.context.object;o.name='Ground';o.data.materials.append(floor);move(o,studio)
def aim(o,point):o.rotation_euler=(Vector(point)-o.location).to_track_quat('-Z','Y').to_euler()
def light(name,loc,power,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    o=bpy.data.objects.new(name,data);studio.objects.link(o);o.location=loc;aim(o,(0,0,1.7))
light('Key • softbox',(-3,-4,6),450,4)
light('Fill',(4,-2,4),250,3)
light('Rim',(1,3,5),550,3)
data=bpy.data.cameras.new('Portrait camera');cam=bpy.data.objects.new('Portrait camera',data);studio.objects.link(cam)
cam.location=(4,-8,3.65);aim(cam,(0,0,1.8));data.type='ORTHO';data.ortho_scale=4.65
scene=bpy.context.scene;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.world.color=(.3,.3,.3)
scene.render.resolution_x=960;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast'
scene.view_settings.exposure=.65
scene.render.image_settings.file_format='PNG'
scene.render.filepath=os.path.join(OUT,'chibi_preview.png')
# Keep the editable asset selected and frame it on opening.
bpy.ops.object.select_all(action='DESELECT')
for c in (model,face):
    for o in c.objects:o.select_set(True)
bpy.context.view_layer.objects.active=head
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=5.5
            area.spaces.active.region_3d.view_location=(0,0,1.8)
            area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'white_chibi.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'white_chibi.glb'),use_selection=True,export_apply=True)
bpy.ops.render.render(write_still=True)
cam.location=(0,-8,2.85);aim(cam,(0,0,1.8));scene.render.filepath=os.path.join(OUT,'chibi_front.png');bpy.ops.render.render(write_still=True)
print('CHIBI_COMPLETE')

