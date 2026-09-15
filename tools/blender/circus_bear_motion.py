"""Editable, baked quadruped animation. Metres, 30 fps, +Y forward.
Grounded IK supports a flexible spine; paw roll and toes give every step weight.
Strike contact is authored at 0.72 s, matching PitBear.ContactSeconds.
"""
from mathutils import Euler
for p in rig.pose.bones: p.rotation_mode='QUATERNION'
rig.animation_data_create(); scene.render.fps=30

def smooth(t):
    t=max(0,min(1,t)); return t*t*(3-2*t)

def pose(name,rot=(0,0,0),loc=(0,0,0),scale=(1,1,1)):
    p=rig.pose.bones[name];p.rotation_quaternion=Euler(rot,'XYZ').to_quaternion();p.scale=scale
    p.location=p.bone.matrix_local.to_3x3().inverted()@Vector(loc) if name=='Root' else Vector(loc)

def orient(name,head,tail):
    p=rig.pose.bones[name];rest=p.bone
    rotation=(rest.tail_local-rest.head_local).rotation_difference(tail-head)@rest.matrix_local.to_quaternion()
    p.matrix=Matrix.LocRotScale(head,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()

def leg(limb,side,target,roll=0,pole_hint=None):
    upper=rig.pose.bones[limb+'Upper.'+side];lower=rig.pose.bones[limb+'Lower.'+side];paw=rig.pose.bones[limb+'Paw.'+side]
    bpy.context.view_layer.update()
    hip=upper.head.copy();direction=target-hip;distance=direction.length;direction.normalize()
    l1=upper.bone.length;l2=lower.bone.length;distance=min(distance,l1+l2-.003)
    along=(l1*l1-l2*l2+distance*distance)/(2*max(.001,distance))
    offset=math.sqrt(max(0,l1*l1-along*along))
    pole=Vector((0,-1 if limb=='Fore' else 1,0)) if pole_hint is None else pole_hint.copy()
    pole-=direction*pole.dot(direction);pole.normalize()
    knee=hip+direction*along+pole*offset;ankle=hip+direction*distance
    orient(upper.name,hip,knee);orient(lower.name,knee,ankle)
    paw.matrix=Matrix.LocRotScale(ankle,Quaternion((1,0,0),roll)@paw.bone.matrix_local.to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()

for title,count in [('Idle',180),('Walk',30),('Run',22),('Alert',90),('Strike',54),('Roar',96)]:
    action=bpy.data.actions.new('Bruno_'+title);action.use_fake_user=True;rig.animation_data.action=action
    for f in range(1,count+2):
        t=(f-1)/count;cycle=t*math.tau;seconds=(f-1)/30
        for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
        targets={(limb,side):bones[limb+'Paw.'+side][0].copy() for limb in ('Fore','Hind') for side in ('L','R')}
        rolls={key:0 for key in targets}
        poles={}
        if title=='Idle':
            breathe=math.sin(cycle*2)
            pose('Root',loc=(.022*math.sin(cycle),0,-.025))
            pose('Pelvis',(0,0,.016*math.sin(cycle)))
            pose('Lumbar',(.015*breathe,0,-.024*math.sin(cycle)))
            pose('Spine',(.018*math.sin(cycle*2-.5),0,.018*math.sin(cycle+.6)),scale=(1+.016*breathe,1+.009*breathe,1+.019*breathe))
            pose('Chest',(-.015*math.sin(cycle*2-.8),0,0),scale=(1+.012*breathe,1,1+.014*breathe))
            look=math.sin(cycle)*smooth(t/.12)*(1-smooth((t-.82)/.18))
            pose('Neck',(.02*math.sin(cycle*2+.5),.06*look,0))
            pose('Head',(.05*math.sin(cycle+.3),.12*look,.024*math.sin(cycle+.7)))
            pose('Jaw',(-.018*max(0,math.sin(cycle*2)),0,0));pose('Tail',(0,0,.13*math.sin(cycle+.5)))
        elif title in ('Walk','Run'):
            run=title=='Run';stance=.31 if run else .58;speed=5.5 if run else 2.1;duration=count/30
            # A heavy rolling walk and an asymmetric lope, with visible shoulder travel.
            travel=speed*duration*stance
            bounce=(-.13+.06*math.sin(cycle*2-.35)) if run else (-.10+.022*math.cos(cycle*2))
            pose('Root',loc=(.016*math.sin(cycle) if run else .040*math.sin(cycle),0,bounce))
            pose('Pelvis',((.095 if run else .024)*math.sin(cycle),.024*math.cos(cycle),(.024 if run else .044)*math.cos(cycle)))
            pose('Lumbar',((-.12 if run else -.035)*math.sin(cycle-.40),.026*math.sin(cycle),-.035*math.cos(cycle-.25)))
            pose('Spine',((.10 if run else .033)*math.sin(cycle-.85),.023*math.sin(cycle+.7),-.036*math.cos(cycle+.5)))
            pose('Chest',((.055 if run else .024)*math.sin(cycle-1.15),-.022*math.sin(cycle),.026*math.cos(cycle+.9)))
            pose('Neck',(-.05*math.sin(cycle-.7),.017*math.sin(cycle),.02*math.cos(cycle)))
            pose('Head',(-.065*math.sin(cycle-1.1),.025*math.sin(cycle-.3),-.018*math.cos(cycle)))
            pose('Jaw',(-.07-.016*math.sin(cycle*2) if run else -.012,0,0))
            pose('Tail',(.055*math.sin(cycle-1),0,.09*math.sin(cycle-.8)))
            phases={('Fore','L'):0,('Fore','R'):.12,('Hind','L'):.49,('Hind','R'):.64} if run else {('Hind','L'):0,('Fore','L'):.25,('Hind','R'):.5,('Fore','R'):.75}
            for key,base in targets.items():
                phase=(t+phases[key])%1
                if phase<stance:
                    support=phase/stance;y=travel*(.5-support);z=.018*math.sin(math.pi*support)
                    roll=.10*(1-smooth(support/.16))-.16*smooth((support-.80)/.20)
                else:
                    swing=(phase-stance)/(1-stance);y=travel*(-.5+smooth(swing));z=(.31 if run else .19)*math.sin(math.pi*swing)**1.15
                    roll=-.24*math.sin(math.pi*swing)+.12*smooth((swing-.65)/.35)
                base.y+=y;base.z+=z;rolls[key]=roll
                if phase>=stance:base.x+=(1 if key[1]=='L' else -1)*.04*math.sin(math.pi*(phase-stance)/(1-stance))
                if key[0]=='Fore':pose('Scapula.'+key[1],(.08*math.sin(math.tau*phase),0,.022*math.sin(math.tau*phase)))
                pose(key[0]+'Toes.'+key[1],(-roll*.65,0,0))
        elif title=='Alert':
            attention=math.sin(math.pi*t)**.7
            pose('Root',loc=(.04*attention,0,-.045*attention))
            pose('Pelvis',(.04*attention,0,0));pose('Lumbar',(-.035*attention,0,0))
            pose('Spine',(.025*math.sin(cycle*2),0,.025*attention))
            pose('Chest',(.04*attention,0,-.02*attention))
            pose('Neck',(-.12*attention,.04*math.sin(cycle)*attention,0));pose('Head',(-.10*attention,.11*math.sin(cycle)*attention,0))
            pose('Jaw',(-.095*math.sin(math.pi*t)**2,0,0))
        elif title=='Strike':
            # Brace, visibly raise the striking paw, accelerate through the victim,
            # follow through across the body, then plant it again. No long held pose.
            wind=smooth(seconds/.47);swing=smooth((seconds-.47)/.25)
            follow=smooth((seconds-.72)/.25);settle=smooth((seconds-1.00)/.70)
            weight=wind*(1-settle);extension=swing*(1-settle)
            pose('Root',loc=(.085*weight,0,-.07*weight))
            pose('Pelvis',(.24*weight,0,-.035*weight))
            pose('Lumbar',(-.10*weight,-.09*weight+.16*extension,.055*weight))
            pose('Spine',(.08*weight,-.13*weight+.27*extension,.08*weight-.16*extension))
            pose('Chest',(.10*weight,-.09*weight+.15*extension,.04*weight-.08*extension))
            pose('Neck',(-.10*weight,0,-.04*extension));pose('Head',(-.14*weight,.08*extension,0));pose('Jaw',(-.40*weight,0,0))
            target=targets[('Fore','R')]
            raised=Vector((-1.04,.90,1.85));contact=Vector((.16,1.73,1.02));across=Vector((.68,1.34,.64))
            target=target.lerp(raised,wind).lerp(contact,swing).lerp(across,follow)
            targets[('Fore','R')]=target.lerp(bones['ForePaw.R'][0],settle)
            rolls[('Fore','R')]=-.40*weight+.58*extension
            poles[('Fore','R')]=Vector((0,-1,0)).lerp(Vector((-1,0,-.65)),weight)
            pose('ForeToes.R',(.18*weight-.28*extension,0,0))
            targets[('Fore','L')].x+=.09*weight;targets[('Fore','L')].y+=.10*weight
            targets[('Hind','R')].y-=.13*weight
        elif title=='Roar':
            rise=smooth(t/.27)*(1-smooth((t-.72)/.28))
            pose('Root',loc=(0,0,-.04*rise));pose('Pelvis',(.86*rise,0,0))
            pose('Lumbar',(.11*rise,0,0));pose('Spine',(-.06*rise,0,0));pose('Chest',(.04*rise,0,0))
            pose('Head',(-.57*rise,0,.035*math.sin(cycle*2)*rise));pose('Jaw',(-.42*rise,0,0))
            for side,s in [('L',1),('R',-1)]:
                target=targets[('Fore',side)];target.y-=.08*rise;target.z+=2.05*rise;target.x+=s*.14*rise
        for (limb,side),target in targets.items():leg(limb,side,target,rolls[(limb,side)],poles.get((limb,side)))
        blink=0
        if title=='Idle':blink=max(max(0,1-abs(t-.23)/.025),max(0,1-abs(t-.78)/.024))
        for side,sign in [('L',1),('R',-1)]:
            pose('Lid.'+side,loc=(0,-.033*blink,0),scale=(1,1+2*blink,1))
            flick=.09*math.sin(cycle*3+.6*sign)*max(0,1-abs(t-(.36 if side=='L' else .67))/.09) if title=='Idle' else .025*math.sin(cycle-1.2+.4*sign)
            pose('Ear.'+side,(flick,0,sign*flick*.45))
        for p in rig.pose.bones:
            p.keyframe_insert('rotation_quaternion',frame=f);p.keyframe_insert('location',frame=f);p.keyframe_insert('scale',frame=f)
    if title in ('Idle','Walk','Run'):
        scene.frame_set(1)
        for p in rig.pose.bones:
            p.keyframe_insert('rotation_quaternion',frame=count+1);p.keyframe_insert('location',frame=count+1);p.keyframe_insert('scale',frame=count+1)
    print('AUTHORED',title,count/30,'seconds',flush=True)
