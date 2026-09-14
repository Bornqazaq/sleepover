"""Baked quadruped animation, executed inside circus_bear.py.
Paws follow ground-space support trajectories. Two-bone IK is solved at authoring
 time, so Unity needs no constraints, runtime IK or Humanoid retargeting.
"""
from mathutils import Euler
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
rig.animation_data_create();scene.render.fps=30

def smooth(t):
    t=max(0,min(1,t));return t*t*(3-2*t)

def pose(name,rot=(0,0,0),loc=(0,0,0),scale=(1,1,1)):
    p=rig.pose.bones[name];p.rotation_quaternion=Euler(rot,'XYZ').to_quaternion();p.scale=scale
    p.location=p.bone.matrix_local.to_3x3().inverted()@Vector(loc) if name=='Root' else Vector(loc)

def orient(name,head,tail):
    p=rig.pose.bones[name];rest=p.bone
    rotation=(rest.tail_local-rest.head_local).rotation_difference(tail-head)@rest.matrix_local.to_quaternion()
    p.matrix=Matrix.LocRotScale(head,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()

def leg(limb,side,target):
    upper=rig.pose.bones[limb+'Upper.'+side];lower=rig.pose.bones[limb+'Lower.'+side];paw=rig.pose.bones[limb+'Paw.'+side]
    bpy.context.view_layer.update()
    hip=upper.head.copy();direction=target-hip;distance=direction.length;direction.normalize()
    l1=upper.bone.length;l2=lower.bone.length;distance=min(distance,l1+l2-.002)
    along=(l1*l1-l2*l2+distance*distance)/(2*max(.001,distance))
    offset=math.sqrt(max(0,l1*l1-along*along))
    pole=Vector((0,-1 if limb=='Fore' else 1,0));pole-=direction*pole.dot(direction);pole.normalize()
    knee=hip+direction*along+pole*offset;ankle=hip+direction*distance
    orient(upper.name,hip,knee);orient(lower.name,knee,ankle)
    paw.matrix=Matrix.LocRotScale(ankle,paw.bone.matrix_local.to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()

for title,count in [('Idle',180),('Walk',30),('Run',24),('Alert',90),('Strike',45),('Roar',96)]:
    action=bpy.data.actions.new('Bruno_'+title);action.use_fake_user=True;rig.animation_data.action=action
    for f in range(1,count+2):
        t=(f-1)/count;cycle=t*math.tau
        for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
        targets={(limb,side):bones[limb+'Paw.'+side][0].copy() for limb in ('Fore','Hind') for side in ('L','R')}
        if title=='Idle':
            breathe=math.sin(cycle*2)
            pose('Root',loc=(.013*math.sin(cycle),0,-.015))
            pose('Spine',(.008*breathe,0,.012*math.sin(cycle)),scale=(1+.008*breathe,1+.005*breathe,1+.008*breathe))
            pose('Neck',(.012*math.sin(cycle*2+.5),0,0))
            pose('Head',(.035*math.sin(cycle),.08*math.sin(cycle),.015*math.sin(cycle+.4)))
            pose('Tail',(0,0,.08*math.sin(cycle)))
        elif title in ('Walk','Run'):
            run=title=='Run';stance=.31 if run else .60;speed=5.5 if run else 2.1;duration=count/30
            travel=speed*duration*stance
            pose('Root',loc=(.025*math.sin(cycle) if not run else .008*math.sin(cycle),0,(-.07+.055*math.sin(cycle*2)) if run else (-.035+.015*math.cos(cycle*2))))
            pose('Pelvis',((.055 if run else .015)*math.sin(cycle),0,(.012 if run else .028)*math.cos(cycle)))
            pose('Spine',((.07 if run else .018)*math.sin(cycle+.6),.013*math.sin(cycle),(.015 if run else -.025)*math.cos(cycle)))
            pose('Neck',(-.03*math.sin(cycle+.6),0,0))
            pose('Head',(-.045*math.sin(cycle+.9),.012*math.sin(cycle),-.008*math.cos(cycle)))
            pose('Jaw',(-.045 if run else 0,0,0));pose('Tail',(.035*math.sin(cycle+.5),0,.04*math.sin(cycle)))
            phases={('Fore','L'):0,('Fore','R'):.07,('Hind','L'):.51,('Hind','R'):.58} if run else {('Hind','L'):0,('Fore','L'):.25,('Hind','R'):.5,('Fore','R'):.75}
            for key,base in targets.items():
                phase=(t+phases[key])%1
                if phase<stance:y=travel*(.5-phase/stance);z=0
                else:
                    swing=(phase-stance)/(1-stance);y=travel*(-.5+smooth(swing));z=(.24 if run else .14)*math.sin(math.pi*swing)**1.35
                base.y+=y;base.z+=z
                if key[0]=='Fore':pose('Scapula.'+key[1],(.045*math.sin(math.tau*phase),0,0))
        elif title=='Alert':
            attention=math.sin(math.pi*t)**.7
            pose('Root',loc=(.035*attention,0,-.025*attention))
            pose('Neck',(-.095*attention,0,0));pose('Head',(-.06*attention,.065*math.sin(cycle)*attention,0))
            pose('Spine',(.016*math.sin(cycle*2),0,.014*attention))
            pose('Jaw',(-.045*math.sin(math.pi*t)**2,0,0))
        elif title=='Strike':
            # 0.55 s contact corresponds to the gameplay lunge/contact gate.
            wind=smooth(t/.19);swing=smooth((t-.19)/.19);settle=smooth((t-.43)/.55)
            weight=wind*(1-settle);extension=swing*(1-settle)
            pose('Root',loc=(.08*weight,0,-.085*weight))
            pose('Spine',(-.045*weight,-.14*weight+.25*extension,.10*weight-.19*extension))
            pose('Neck',(-.035*weight,0,-.035*extension));pose('Head',(-.04*weight,.04*extension,0));pose('Jaw',(-.33*weight,0,0))
            target=targets[('Fore','R')]
            target.x+=(-.18*wind+.65*swing)*(1-settle)
            target.y+=(-.40*wind+1.48*swing)*(1-settle)
            target.z+=(.69*wind-.20*swing)*(1-settle)
            targets[('Fore','L')].x+=.05*weight
        elif title=='Roar':
            rise=smooth(t/.27)*(1-smooth((t-.72)/.28))
            pose('Root',loc=(0,0,-.04*rise));pose('Pelvis',(1.03*rise,0,0))
            pose('Spine',(-.14*rise,0,0));pose('Head',(-.57*rise,0,.035*math.sin(cycle*2)*rise))
            pose('Jaw',(-.42*rise,0,0))
            for side,s in [('L',1),('R',-1)]:
                target=targets[('Fore',side)];target.y-=.08*rise;target.z+=2.05*rise;target.x+=s*.14*rise
        for (limb,side),target in targets.items():leg(limb,side,target)
        # Blinks are brief and staggered; the tiny lid bones remain part of the generic rig.
        blink=max(0,1-abs(t-.23)/.025) if title=='Idle' else 0
        if title=='Idle':blink=max(blink,max(0,1-abs(t-.78)/.024))
        for side in ('L','R'):pose('Lid.'+side,loc=(0,-.033*blink,0),scale=(1,1+2.0*blink,1))
        for p in rig.pose.bones:
            p.keyframe_insert('rotation_quaternion',frame=f);p.keyframe_insert('location',frame=f);p.keyframe_insert('scale',frame=f)
    # Exact seam, including IK and eyelid scale.
    if title in ('Idle','Walk','Run'):
        scene.frame_set(1)
        for p in rig.pose.bones:
            p.keyframe_insert('rotation_quaternion',frame=count+1);p.keyframe_insert('location',frame=count+1);p.keyframe_insert('scale',frame=count+1)
    print('AUTHORED',title,count/30,'seconds',flush=True)
