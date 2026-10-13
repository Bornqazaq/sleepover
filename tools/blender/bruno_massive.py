"""IGR-757: sculpt a broad, low brown bear around existing joint centres.

Union and retopologize the mass before skinning. This avoids inflated sleeves
and preserves the anatomical bend planes of the corrected Meshy walk.
"""
import bpy, math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
ROOT=Path(__file__).resolve().parents[2]
path=ROOT/'tools/blender/bruno_meshy_body.py'
code=path.read_text()
changes={
 "OUT = ROOT / 'art-source/BrunoMeshy'":"OUT = ROOT / 'art-source/BrunoMeshy/heavy-body'",
 "SCENE_NAME = 'Bruno_Meshy_2026_10'":"SCENE_NAME = 'Bruno_HeavyBodySource'",
 "Bruno_Meshy_BodyOnly":"Bruno_HeavyBodyOnly",
 "(0,-.10,1.32), (.55,.91,.51)":"(0,-.10,1.16), (.80,1.01,.72)",
 "(0,-.91,1.27), (.51,.54,.48)":"(0,-.89,1.14), (.73,.66,.65)",
 "(0,.30,1.63), (.55,.68,.49)":"(0,.30,1.56), (.75,.73,.57)",
 "(0,.43,1.78), (.36,.44,.30)":"(0,.43,1.74), (.52,.52,.30)",
 "(0,.59,1.40), (.39,.35,.38)":"(0,.59,1.22), (.57,.49,.53)",
 "(0,.96,1.55), (.375,.59,.33)":"(0,.96,1.50), (.46,.58,.37)",
 "(0,1.12,1.40), (.265,.36,.22)":"(0,1.12,1.34), (.35,.37,.27)",
 "(sign*.215,1.40,1.52), (.14,.26,.19)":"(sign*.215,1.40,1.52), (.12,.25,.17)",
 "(.088,.12,.043)":"(.088,.12,.025)",
 "x = sign*.53":"x = sign*.61",
 "(1.73,.36,.195,.235)":"(1.73,.36,.31,.35)",
 "(1.48,.40,.235,.28)":"(1.48,.40,.40,.44)",
 "(1.24,.32,.215,.24)":"(1.24,.36,.40,.43)",
 "(1.03,.22,.172,.19)":"(1.03,.31,.38,.40)",
 "(.91,.20,.147,.16)":"(.91,.30,.38,.39)",
 "(.80,.27,.165,.182)":"(.80,.32,.375,.39)",
 "(.63,.34,.151,.17)":"(.63,.37,.35,.37)",
 "(.46,.42,.118,.14)":"(.46,.42,.32,.34)",
 "(.34,.45,.105,.119)":"(.34,.45,.29,.30)",
 "(.25,.47,.122,.143)":"(.25,.48,.27,.28)",
 "(1.53,-.92,.205,.26)":"(1.53,-.92,.35,.39)",
 "(1.39,-.91,.265,.32)":"(1.39,-.91,.43,.45)",
 "(1.17,-.85,.248,.295)":"(1.17,-.86,.42,.44)",
 "(.98,-.73,.195,.223)":"(.98,-.88,.39,.41)",
 "(.84,-.65,.159,.173)":"(.84,-.86,.38,.40)",
 "(.72,-.74,.155,.165)":"(.72,-.90,.365,.385)",
 "(.57,-.88,.133,.148)":"(.57,-.91,.34,.36)",
 "(.40,-1.01,.101,.12)":"(.40,-.95,.30,.32)",
 "(.32,-1.005,.103,.12)":"(.32,-.95,.28,.29)",
 "(.22,-.96,.126,.14)":"(.22,-.96,.265,.28)",
 "(.224,.284,.134)":"(.274,.300,.145)",
 "(0,1.798,1.263)":"(0,1.798,1.335)",
 "(0,1.911,1.326)":"(0,1.911,1.368)",
 "1.306-.009":"1.373-.009",
 "(sign*.171,1.874,1.310)":"(sign*.171,1.874,1.336)",
 "(sign*.166,1.892,1.335)":"(sign*.166,1.892,1.350)",
 "(sign*.156,1.913,1.376)":"(sign*.156,1.913,1.391)",
}
changes.update({
    "y=2.102-abs(x)*.10":"y=2.077-abs(x)*.10",
    "[.013,.011,.006]":"[.010,.008,.004]",
    "(.184,.249,.085)":"(.180,.247,.049)",
    "(sign*.153,1.984,1.280)":"(sign*.153,1.984,1.320)",
    "(.106,.135,.10)":"(.092,.105,.065)",
    "Vector((sign*.276,1.235,1.902))":"Vector((sign*.285,1.255,1.895))",
    "((.025,-.042),(.70,-.039),(1,-.005),(.86,.025),(.56,.009),(.025,-.016))":"((.025,-.024),(.70,-.026),(1,-.005),(.74,.016),(.40,.010),(.025,.004))",
    "math.cos(a)*.108*radius":"math.cos(a)*.091*radius",
    "math.sin(a)*.115*radius":"math.sin(a)*.097*radius",
    "iris_mat = material('Iris_amber', (.28, .105, .016), .26)":"iris_mat = material('Iris_amber', (.050, .023, .009), .23)",
    "(.012,.003,.012)":"(.009,.003,.009)",
    "(.007,.002,.009)":"(.007,.002,.007)",
    "(.026*math.cos(a))":"(.024*math.cos(a))",
    "(.017 if upper else -.016)":"(.013 if upper else -.012)",
    "(.159,.079,.083)":"(.159,.072,.078)",
})
for old,new in changes.items():
    assert old in code,old
    code=code.replace(old,new)
exec(compile(code,str(path),'exec'),{'__file__':str(path),'__name__':'__main__'})
anatomy={};ap=ROOT/'tools/blender/bruno_anatomy.py'
exec(compile(ap.read_text(),str(ap),'exec'),anatomy)
source=bpy.data.objects['Bruno_HeavyBodyOnly']
for v in source.data.vertices:v.co=anatomy['remap'](v.co)
source.data.update()
for a in list(bpy.data.actions):
    if a.name.startswith('Bruno_'):bpy.data.actions.remove(a)
path=ROOT/'tools/blender/bruno_meshy_finish.py'
exec(compile(path.read_text(),str(path),'exec'),{'__file__':str(path),'__name__':'__main__','BRUNO_SOURCE':'Bruno_HeavyBodyOnly','BRUNO_REMAP':anatomy['remap'],'BRUNO_UNMAP':anatomy['unmap']})
scene=bpy.data.scenes['Bruno_Finished'];body=bpy.data.objects['Bruno_Body'];rig=bpy.data.objects['BrunoRig']
bones={b.name:(b.head_local.copy(),b.tail_local.copy()) for b in rig.data.bones}
motion=(ROOT/'tools/blender/circus_bear_motion.py').read_text().replace(
    "[('Idle',180),('Walk',24),('Run',18),('Alert',90),('Strike',42),('Roar',96)]",
    "[('Run',18),('Alert',90),('Strike',42),('Roar',96)]")
motion=motion.replace("targets[key].z+=.20*lift*(1-settle)","targets[key].z+=(0 if key == ('Fore','L') else .14*lift*(1-settle))")
motion=motion.replace("targets[('Fore','L')].x+=.07*weight","targets[('Fore','L')]=bones['ForePaw.L'][0].copy();rolls[('Fore','L')]=0")
motion=motion.replace("pose('Tail',(.025*weight,0,-.12*weight+.18*extension))",
    "pose('Tail',(.025*weight,0,-.12*weight+.18*extension));scapula=rig.pose.bones['Scapula.R'];scapula.location=scapula.bone.matrix_local.to_3x3().inverted()@Vector((0,.16*extension,0))")
exec(compile(motion,'circus_bear_motion.py','exec'),globals())
rig.animation_data.action=bpy.data.actions['Bruno_Walk'];scene.frame_set(5)
body['anatomy_revision']='IGR-757: deep barrel, wide shoulder mantle, thick fore/hind limbs'
print('MASSIVE_BODY_READY',tuple(body.dimensions))

# The game's faster authored run rolls its feet. Compensate at the ankle from
# the actual sole surface, then solve the two fixed-length leg segments again.
# This keeps the larger paws above the floor without scaling or stretching.
rig.animation_data.action=bpy.data.actions['Bruno_Run']
soles={(limb,side):[v.index for v in body.data.vertices if v.co.z<.40
    and (v.co.x>0)==(side=='L') and (v.co.y>-.2)==(limb=='Fore')]
    for limb in ('Fore','Hind') for side in ('L','R')}
previous={}
for f in range(1,20):
    scene.frame_set(f)
    evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    floors={key:min(mesh.vertices[i].co.z for i in ids) for key,ids in soles.items()}
    evaluated.to_mesh_clear()
    for (limb,side),floor in floors.items():
        if floor>=-.00001:continue
        paw=rig.pose.bones[limb+'Paw.'+side]
        target=paw.head.copy();target.z+=-floor+.0005
        roll=(paw.matrix.to_quaternion()@paw.bone.matrix_local.to_quaternion().inverted()).to_euler().x
        leg(limb,side,target,roll)
    for p in rig.pose.bones:
        if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
        previous[p.name]=p.rotation_quaternion.copy()
        for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=f)
rig.animation_data.action=bpy.data.actions['Bruno_Walk'];scene.frame_set(5)


for title in ('Run','Strike'):
    rig.animation_data.action=bpy.data.actions['Bruno_'+title]
    for f in range(1,int(rig.animation_data.action.frame_range.y)+1):
        scene.frame_set(f)
        for iteration in range(2):
            ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh()
            floor={key:min(me.vertices[i].co.z for i in ids) for key,ids in soles.items()};ev.to_mesh_clear()
            for key,z in floor.items():
                planted=title=='Strike' and key==('Fore','L')
                if z>=-.00001 and not planted:continue
                paw=rig.pose.bones[key[0]+'Paw.'+key[1]];target=paw.head.copy();target.z-=z
                roll=0 if planted else (paw.matrix.to_quaternion()@paw.bone.matrix_local.to_quaternion().inverted()).to_euler().x
                leg(*key,target,roll)
        for p in rig.pose.bones:
            for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=f)


# A short-legged bear drops its shoulder to keep the supporting forepaw planted
# through the wind-up; it must not hover with all three other paws lifted.
rig.animation_data.action=bpy.data.actions['Bruno_Strike']
for f in range(1,44):
    scene.frame_set(f)
    for iteration in range(3):
        ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh()
        floor=min(me.vertices[i].co.z for i in soles[('Fore','L')]);ev.to_mesh_clear()
        if floor<.0001:break
        targets={key:rig.pose.bones[key[0]+'Paw.'+key[1]].head.copy() for key in soles}
        rolls={key:(rig.pose.bones[key[0]+'Paw.'+key[1]].matrix.to_quaternion()@rig.data.bones[key[0]+'Paw.'+key[1]].matrix_local.to_quaternion().inverted()).to_euler().x for key in soles}
        root=rig.pose.bones['Root'];root.location+=root.bone.matrix_local.to_3x3().inverted()@Vector((0,0,-floor))
        bpy.context.view_layer.update();targets[('Fore','L')].z-=floor
        for key,target in targets.items():leg(*key,target,rolls[key])
    for p in rig.pose.bones:
        for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=f)

path=ROOT/'tools/blender/bruno_meshy_retarget.py'
exec(compile(path.read_text(),str(path),'exec'),globals())
