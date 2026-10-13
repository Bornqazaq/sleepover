"""Bake a restrained shared PBR palette into the preserved body UVs."""
import bpy, json, math
import numpy as np
from pathlib import Path

ROOT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover')
OUT=ROOT/'art-source/BrunoMeshy/polish-v5'
o=bpy.data.objects['Bruno_Body'];me=o.data
islands=json.loads((OUT/'body-islands-before.json').read_text())

def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

colors=np.ones((len(me.vertices),4),dtype=np.float32)
rough=np.full((len(me.vertices),4),.86,dtype=np.float32);rough[:,3]=1
for idx,part in enumerate(islands):
    ids=part['ids'];p=np.array([me.vertices[i].co[:] for i in ids]);x,y,z=p.T
    if idx==0:
        c=np.tile([.075,.032,.012],(len(ids),1))
        mantle=smooth(1.05,1.60,z)*np.exp(-((y-.34)/1.12)**2)*.62
        c=c*(1-mantle[:,None])+np.array([.125,.063,.026])*mantle[:,None]
        muzzle=smooth(1.62,1.93,y-np.abs(x)*.26)*(1-smooth(1.26,1.44,z))
        c=c*(1-muzzle[:,None])+np.array([.145,.073,.030])*muzzle[:,None]
        foot=(1-smooth(.16,.52,z))*.79
        c=c*(1-foot[:,None])+np.array([.018,.010,.006])*foot[:,None]
        c*=1+.032*np.sin(y[:,None]*5+x[:,None]*3)*np.cos(z[:,None]*3)
    elif idx==1 or idx==43:
        c=np.tile([.012,.008,.005],(len(ids),1));rough[ids,:3]=.32
    elif idx==2:
        c=np.tile([.008,.0025,.002],(len(ids),1));rough[ids,:3]=.73
    elif idx==3:
        c=np.tile([.105,.048,.018],(len(ids),1))
        blend=smooth(.79,.93,z)*.40
        c=c*(1-blend[:,None])+np.array([.15,.074,.030])*blend[:,None]
    elif idx in (4,5,10,12,13,14):
        c=np.tile([.005,.0028,.0016],(len(ids),1));rough[ids,:3]=.19 if idx in (4,5,10,12) else .5
    elif idx in (6,7,18,20):
        c=np.tile([.024,.012,.007],(len(ids),1));rough[ids,:3]=.68
    elif idx==8:
        c=np.tile([.12,.035,.031],(len(ids),1));rough[ids,:3]=.63
    elif idx in (9,11):
        c=np.tile([.21,.080,.013],(len(ids),1));rough[ids,:3]=.24
    elif idx in (15,16):
        c=np.tile([.075,.032,.012],(len(ids),1))
        inset=smooth(1.225,1.29,y)*.85
        c=c*(1-inset[:,None])+np.array([.030,.013,.007])*inset[:,None]
    elif idx in (17,19):
        c=np.tile([.078,.033,.012],(len(ids),1))
    elif 21<=idx<=40:
        t=(y-y.min())/np.ptp(y)
        c=np.array([.105,.067,.033])*(1-t[:,None])+np.array([.33,.27,.17])*t[:,None]
        c*=.93+.08*math.sin(idx*7.13);rough[ids,:3]=.49
    else:
        t=(z-z.min())/max(np.ptp(z),.001)
        c=np.array([.57,.47,.30])*(1-t[:,None])+np.array([.37,.27,.135])*t[:,None]
        rough[ids,:3]=.42
    colors[ids,:3]=c

for name in ('V5Color','V5Roughness'):
    if me.color_attributes.get(name):me.color_attributes.remove(me.color_attributes[name])
ca=me.color_attributes.new(name='V5Color',type='FLOAT_COLOR',domain='POINT');ca.data.foreach_set('color',colors.ravel())
ra=me.color_attributes.new(name='V5Roughness',type='FLOAT_COLOR',domain='POINT');ra.data.foreach_set('color',rough.ravel())
mat=me.materials[0].copy();mat.name='Bruno_V5_Body';me.materials[0]=mat
nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
for layer,socket in [('V5Color','Base Color'),('V5Roughness','Roughness')]:
    node=nt.nodes.new('ShaderNodeVertexColor');node.layer_name=layer
    nt.links.new(node.outputs['Color'],bs.inputs[socket])
bs.inputs['Specular IOR Level'].default_value=.30
bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
s=bpy.context.scene;s.cycles.samples=8
s.render.bake.use_pass_direct=False;s.render.bake.use_pass_indirect=False;s.render.bake.use_pass_color=True;s.render.bake.margin=10
images={}
for label,bake,size in [('BaseColor','DIFFUSE',2048),('Roughness','ROUGHNESS',1024)]:
    im=bpy.data.images.new('Bruno_V5_'+label,width=size,height=size,alpha=False)
    if label=='Roughness':im.colorspace_settings.name='Non-Color'
    node=nt.nodes.new('ShaderNodeTexImage');node.image=im;nt.nodes.active=node
    bpy.ops.object.bake(type=bake)
    im.filepath_raw=str(OUT/('Bruno_'+label+'.png'));im.file_format='PNG';im.save();im.pack()
    images[label]=node
for label,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:nt.links.new(images[label].outputs['Color'],bs.inputs[socket])
for name in ('V5Color','V5Roughness'):me.color_attributes.remove(me.color_attributes[name])
# The old normal texture stays in the same UV space, with a quieter fur grain.
for node in nt.nodes:
    if node.type=='NORMAL_MAP':node.inputs['Strength'].default_value=.40
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))
print('V5 PBR MAPS BAKED')
