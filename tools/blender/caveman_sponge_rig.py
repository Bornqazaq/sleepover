"""Animation rig, authored skin weights and a non-production demonstration action."""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/CharacterSandbox/CavemanSponge'
scene=bpy.context.scene
col=bpy.data.collections['01_CHARACTER']
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
old=bpy.data.objects.get('CavemanSponge_Rig')
if old:
    for ob in col.objects:
        if ob.type=='MESH':
            for mod in list(ob.modifiers):
                if mod.type=='ARMATURE':ob.modifiers.remove(mod)
            ob.vertex_groups.clear();ob.parent=None
    bpy.data.objects.remove(old,do_unlink=True)
arm=bpy.data.armatures.new('CavemanSponge_Skeleton');rig=bpy.data.objects.new('CavemanSponge_Rig',arm);col.objects.link(rig)
rig.show_in_front=True;arm.display_type='OCTAHEDRAL'
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
def bone(name,head,tail,parent=None,deform=True):
    b=arm.edit_bones.new(name);b.head=head;b.tail=tail;b.use_deform=deform
    if parent:b.parent=arm.edit_bones[parent]
    return b
bone('Root',(0,0,.05),(0,0,.35),deform=False)
bone('Hips',(0,0,1.04),(0,0,1.39),'Root')
bone('Spine',(0,0,1.39),(0,0,1.80),'Hips')
bone('Chest',(0,0,1.80),(0,0,2.20),'Spine')
bone('Neck',(0,0,2.20),(0,0,2.38),'Chest')
bone('Head',(0,0,2.38),(0,0,3.12),'Neck')
bone('Jaw',(0,-.57,2.30),(0,-1.02,1.77),'Head')
bone('Loincloth',(0,-.53,1.00),(0,-.60,.36),'Hips')
chains={};limb_chains={}
for side,s in [('L',1),('R',-1)]:
    m=lambda p:Vector((s*p[0],p[1],p[2]))
    shoulder=m((1.03,0,2.10));elbow=m((1.70,-.015,1.77));wrist=m((2.38,-.04,1.40));palm=m((2.58,-.04,1.33))
    bone('Shoulder.'+side,m((.40,0,2.12)),shoulder,'Chest')
    bone('UpperArm.'+side,shoulder,elbow,'Shoulder.'+side)
    bone('Forearm.'+side,elbow,wrist,'UpperArm.'+side)
    bone('Hand.'+side,wrist,palm,'Forearm.'+side)
    limbs=['UpperArm.'+side,'Forearm.'+side,'Hand.'+side]
    fingers={'Thumb':[(2.43,-.04,1.38),(2.26,-.07,1.28),(2.22,-.10,1.12)],
             'Index':[(2.59,-.04,1.42),(2.84,-.05,1.36),(3.00,-.09,1.20)],
             'Middle':[(2.63,-.04,1.34),(2.86,-.07,1.16),(2.88,-.12,1.00)],
             'Ring':[(2.58,-.04,1.27),(2.70,-.10,1.06),(2.67,-.15,.90)]}
    for name,ps in fingers.items():
        ps=list(map(m,ps))
        a=name+'1.'+side;b=name+'2.'+side
        bone(a,ps[0],ps[1],'Hand.'+side);bone(b,ps[1],ps[2],a);limbs.extend([a,b])
    chains['Arm.'+side]=limbs
    hip=m((.73,.04,1.03));knee=m((1.02,-.025,.59));ankle=m((1.24,-.04,.22));toe=m((1.47,-.22,.13))
    bone('UpperLeg.'+side,hip,knee,'Hips');bone('LowerLeg.'+side,knee,ankle,'UpperLeg.'+side)
    bone('Foot.'+side,ankle,toe,'LowerLeg.'+side)
    limbs=['UpperLeg.'+side,'LowerLeg.'+side,'Foot.'+side]
    for i in range(3):
        name='Toe%s.%s'%(i+1,side)
        bone(name,m((1.48,-.29+i*.135,.14)),m((1.86-.035*i,-.33+i*.135,.102)),'Foot.'+side);limbs.append(name)
    chains['Leg.'+side]=limbs
    bone('Eye.'+side,(s*.4,-.70,2.68 if side=='L' else 2.72),(s*.4,-1.10,2.68 if side=='L' else 2.72),'Head')
    bone('Hand_IK.'+side,wrist,wrist+Vector((0,-.28,0)),'Root',False)
    bone('ElbowPole.'+side,elbow+Vector((0,-.9,0)),elbow+Vector((0,-.9,.18)),'Root',False)
    bone('Foot_IK.'+side,ankle,ankle+Vector((0,-.30,0)),'Root',False)
    bone('KneePole.'+side,knee+Vector((0,-.8,0)),knee+Vector((0,-.8,.18)),'Root',False)
bpy.ops.object.mode_set(mode='OBJECT')

deform=arm.collections.new('Deform / FK')
face=arm.collections.new('Face / Jaw / Eyes')
fingers=arm.collections.new('Fingers + Toes')
ikcol=arm.collections.new('Optional IK controls')
for b in arm.bones:
    if not b.use_deform:ikcol.assign(b)
    elif b.name.startswith(('Eye','Jaw')):face.assign(b)
    elif b.name.startswith(('Thumb','Index','Middle','Ring','Toe')):fingers.assign(b)
    else:deform.assign(b)
    b.color.palette='THEME04' if b.name.endswith('.L') else 'THEME03' if b.name.endswith('.R') else 'THEME02'
for p in rig.pose.bones:p.rotation_mode='QUATERNION'

# Controls are optional. Rest/default is FK, ready for retargeting or direct posing.
for side in ['L','R']:
    for limb,pbone,target,pole in [('Arm','Forearm.','Hand_IK.','ElbowPole.'),('Leg','LowerLeg.','Foot_IK.','KneePole.')]:
        prop=limb+'_IK_'+side;rig[prop]=0.0
        rig.id_properties_ui(prop).update(min=0,max=1,description='0 = FK / retargeted animation, 1 = IK target')
        con=rig.pose.bones[pbone+side].constraints.new('IK');con.name='Optional '+limb+' IK'
        con.target=rig;con.subtarget=target+side;con.pole_target=rig;con.pole_subtarget=pole+side
        con.chain_count=2;con.use_stretch=False;con.influence=0
        # Calibrated against the authored elbow/knee plane; FK->IK rest drift <0.3 mm.
        con.pole_angle={('Arm','L'):-.4417864669110645,('Arm','R'):-2.6998061866787286,
                        ('Leg','L'):-.9817477042468106,('Leg','R'):-2.1598449493429825}[(limb,side)]
        fc=con.driver_add('influence');d=fc.driver;d.type='AVERAGE';v=d.variables.new();v.name='ik'
        v.type='SINGLE_PROP';v.targets[0].id=rig;v.targets[0].data_path='["'+prop+'"]'
        for name in ([('UpperArm.'+side),('Forearm.'+side)] if limb=='Arm' else [('UpperLeg.'+side),('LowerLeg.'+side)]):
            rig.pose.bones[name].ik_stretch=0
    for name in ['Hand','Foot']:
        ctrl=rig.pose.bones[name+'_IK.'+side]
        rig.pose.bones[name+'.'+side]['IK_note']='Position follows IK; rotate this bone for hand/foot orientation.'
for side in ['L','R']:
    rig['Blink_'+side]=0.0;rig.id_properties_ui('Blink_'+side).update(min=0,max=1,description='Upper eyelid closure')
    key=bpy.data.objects['UpperLid.'+side].data.shape_keys.key_blocks['Blink']
    if key.id_data.animation_data:key.id_data.animation_data_clear()
    fc=key.driver_add('value');fc.driver.type='AVERAGE';v=fc.driver.variables.new();v.type='SINGLE_PROP';v.name='blink'
    v.targets[0].id=rig;v.targets[0].data_path='["Blink_'+side+'"]'

def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def dist_seg(p,a,b):
    ab=b-a;t=max(0,min(1,(p-a).dot(ab)/ab.length_squared));return (p-(a+ab*t)).length
def chain_weights(p,names,width):
    ds=[(n,dist_seg(p,arm.bones[n].head_local,arm.bones[n].tail_local)) for n in names]
    dmin=min(d for n,d in ds)
    vals=sorted([(n,math.exp(-min(700,(d*d-dmin*dmin)/(2*width*width)))) for n,d in ds],key=lambda v:-v[1])[:4]
    vals=[(n,w) for n,w in vals if w>.004];total=sum(w for n,w in vals)
    return [(n,w/total) for n,w in vals]
def weights(ob,p):
    region=ob.get('bind_region','Head')
    if region.startswith(('Arm.','Leg.')):
        return chain_weights(p,chains[region],.048 if region.startswith('Arm') and abs(p.x)>2.35 else .070)
    if region=='Body':
        # The face and head form one block; the lower body bends gently at the waist.
        h=smooth(1.40,2.25,p.z);pelvis=1-smooth(.98,1.51,p.z)
        remaining=max(0,1-h-pelvis);chest=remaining*smooth(1.35,1.92,p.z)
        return [(n,w) for n,w in [('Hips',pelvis),('Spine',remaining-chest),('Chest',chest),('Head',h)] if w>.0001]
    if region=='Cloth':
        t=1-smooth(.82,1.0,p.z);return [('Hips',1-t),('Loincloth',t)]
    if region in ('Lip','Mouth'):
        jaw=(1-smooth(2.18,2.37,p.z))*(1-smooth(.55,.82,abs(p.x)))*.93
        return [('Head',1-jaw),('Jaw',jaw)]
    return [(region,1)]

total=0;max_influences=0;max_error=0
for ob in col.objects:
    if ob.type!='MESH':continue
    ob.vertex_groups.clear()
    for b in arm.bones:
        if b.use_deform:ob.vertex_groups.new(name=b.name)
    for v in ob.data.vertices:
        ws=[(n,w) for n,w in weights(ob,v.co) if w>1e-8];norm=sum(w for n,w in ws)
        assert norm>0,(ob.name,v.index)
        for n,w in ws:ob.vertex_groups[n].add([v.index],w/norm,'REPLACE')
        max_influences=max(max_influences,len(ws));total+=1
    ob.parent=rig
    mod=ob.modifiers.new('Caveman Sponge skin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=True
    # Armature first; thickness follows deformed lids and leather.
    bpy.context.view_layer.objects.active=ob
    while ob.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    for v in ob.data.vertices:max_error=max(max_error,abs(sum(g.weight for g in v.groups)-1))

# Stable UVs for later texture painting/baking. Blender materials remain procedural.
bpy.ops.object.select_all(action='DESELECT')
meshes=[ob for ob in col.objects if ob.type=='MESH']
for ob in meshes:ob.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015)
bpy.ops.object.mode_set(mode='OBJECT')

# Editable, named action with no dependency on production animation assets.
rig.animation_data_create()
action=bpy.data.actions.new('Sandbox • wave, bend, fingers, jaw and blink');action.use_fake_user=True;rig.animation_data.action=action
def reset():
    for p in rig.pose.bones:p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
    for side in ['L','R']:rig['Blink_'+side]=0
def rotate(name,axis,angle):
    b=arm.bones[name];local_axis=b.matrix_local.to_quaternion().inverted()@Vector(axis)
    rig.pose.bones[name].rotation_quaternion=Quaternion(local_axis,angle)
def key(frame):
    for p in rig.pose.bones:
        p.keyframe_insert('location',frame=frame,group=p.name);p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
    for side in ['L','R']:rig.keyframe_insert('["Blink_'+side+'"]',frame=frame,group='Facial controls')
reset();key(1)
rotate('UpperArm.L',(0,1,0),-.48);rotate('Forearm.L',(0,1,0),-1.25)
rotate('Hand.L',(1,0,0),.20);rotate('Head',(0,1,0),-.05)
for finger in ['Middle','Ring']:rotate(finger+'1.L',(1,0,0),-.35);rotate(finger+'2.L',(1,0,0),-.45)
key(25)
rotate('Hand.L',(1,0,0),-.22);rig['Blink_L']=1;rig['Blink_R']=1;key(34)
rotate('Hand.L',(1,0,0),.22);rig['Blink_L']=0;rig['Blink_R']=0;key(41)
rotate('Hand.L',(1,0,0),-.18);key(49)
reset();key(65)
rotate('UpperLeg.L',(1,0,0),-.34);rotate('LowerLeg.L',(1,0,0),.65)
rotate('Foot.L',(1,0,0),-.2);rotate('Spine',(0,1,0),.075)
rotate('UpperArm.R',(0,0,1),-.30);rotate('Forearm.R',(0,0,1),-.48)
rotate('Jaw',(1,0,0),.12);rotate('Loincloth',(1,0,0),-.12)
for finger in ['Thumb','Index','Middle','Ring']:
    rotate(finger+'1.R',(1,0,0),.6);rotate(finger+'2.R',(1,0,0),.8)
key(85)
reset();key(110);key(120)
scene.timeline_markers.clear()
for name,frame in [('REST / A-POSE',1),('ARM + FINGERS',25),('BLINK',34),('LEG + JAW',85),('REST',120)]:scene.timeline_markers.new(name,frame=frame)
scene.frame_set(1)

# Embedded instructions travel with the .blend file.
text=bpy.data.texts.get('START_HERE • управление') or bpy.data.texts.new('START_HERE • управление')
text.clear();text.write('''CAVEMAN SPONGE — отдельный Blender sandbox, 14.10.2026

Модель создана по присланной картинке. Спина и толщина — интерпретация.
В Unity/игру ничего не импортировано. Исходный персонаж игры не менялся.

Выделить CavemanSponge_Rig → Pose Mode. По умолчанию FK.
Root — целиком; Hips/Spine/Chest/Head — корпус; Jaw — нижняя губа/челюсть.
Eye.L/R — взгляд. UpperArm/Forearm/Hand и UpperLeg/LowerLeg/Foot — суставы.
Thumb/Index/Middle/Ring — по 2 фаланги, Toe — отдельные пальцы стопы.
Object Properties → Custom Properties → Blink_L/R (0–1) — веки.
Arm_IK_L/R и Leg_IK_L/R (0–1) включают дополнительные IK-цели.
При FK→IK сначала совмещать цель с суставом; это не автоматический snap.

Таймлайн 1–120: проверочная анимация (взмах, пальцы, шаг, челюсть, моргание).
Кадр 1 — исходная поза. Alt+G / Alt+R в Pose Mode сбрасывают выбранные кости.
Материалы и микрорельеф процедурные; цвет крупных пор в SpongeColor.
Есть UV для дальнейшей покраски/запекания. На всех вершинах <=4 веса.
Для экспорта в другой сервис потребуется проверить его требования и запечь
процедурные материалы; совместимость с Meshy этим проходом не проверена.
''')
rig['Readme']='See text block START_HERE • управление. FK default, optional IK; frame 1 neutral.'
validation={'mesh_objects':len(meshes),'vertices':total,'bones':len(arm.bones),'deform_bones':sum(b.use_deform for b in arm.bones),'max_influences':max_influences,'max_weight_error':max_error,'unweighted_vertices':sum(1 for ob in meshes for v in ob.data.vertices if not v.groups),'unity_imported':False,'animation':'Sandbox • wave, bend, fingers, jaw and blink'}
(OUT/'validation.json').write_text(json.dumps(validation,indent=2),encoding='utf8')
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CavemanSponge.blend'))
print(json.dumps(validation))
