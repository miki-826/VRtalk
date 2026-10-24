import bpy, json, os
from mathutils import Vector
project=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.open_mainfile(filepath=os.path.join(project,'model','Botanica','Botanica_Cafe.blend'))
out=os.path.join(project,'QuestAI','Assets','Art','Botanica')
os.makedirs(out,exist_ok=True)
scene=bpy.context.scene; dg=bpy.context.evaluated_depsgraph_get()
def v3(v):return {'x':round(v[0],6),'y':round(v[2],6),'z':round(v[1],6)}
materials=[]
for m in bpy.data.materials:
 if not m.use_nodes:continue
 p=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
 if not p:continue
 c=p.inputs['Base Color'].default_value
 materials.append({'name':m.name,'color':{'r':c[0],'g':c[1],'b':c[2],'a':1},'metallic':p.inputs['Metallic'].default_value,'roughness':p.inputs['Roughness'].default_value,'glass':p.inputs['Transmission Weight'].default_value>.5,'emission':m.name=='MAT_WarmBulb'})
groups={};colliders=[];total=0
for o in scene.objects:
 if o.type not in {'MESH','FONT'}:continue
 ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();matrix=o.matrix_world;normalmat=matrix.to_3x3().inverted().transposed();col=o.users_collection[0].name
 for tri in me.loop_triangles:
  mat=me.materials[tri.material_index].name if me.materials else 'MAT_Ceramic';key=(col,mat)
  group=groups.setdefault(key,{'name':col+'_'+mat,'material':mat,'vertices':[],'normals':[],'uv':[],'triangles':[]})
  idx=len(group['vertices'])
  for li in tri.loops:
   loop=me.loops[li];group['vertices'].append(v3(matrix@me.vertices[loop.vertex_index].co));group['normals'].append(v3((normalmat@me.corner_normals[li].vector).normalized()));uv=me.uv_layers.active.data[li].uv if me.uv_layers.active else (0,0);group['uv'].append({'x':uv[0],'y':uv[1]})
  group['triangles'] += [idx,idx+2,idx+1];total+=1
 if o.name.startswith(('ENV_Floor','Wall_','Window_Glass','Counter_Body','Table_Top','Chair_Seat','Chair_Back','Lounge_','Back_Cabinet')):
  points=[matrix@Vector(p) for p in o.bound_box];lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
  colliders.append({'name':o.name,'center':v3((lo+hi)/2),'size':v3(hi-lo)})
 ev.to_mesh_clear()
with open(os.path.join(out,'BotanicaGeometry.json'),'w',encoding='utf8') as f:json.dump({'materials':materials,'meshes':list(groups.values()),'colliders':colliders,'triangles':total},f,separators=(',',':'))
print('BOTANICA_EXPORT',total,'triangles',len(groups),'mesh groups')

