"""Repair Bruno's rear: elbows hang below shoulders instead of folding upwards.
Run in the existing Blender scene. Only the Roar forelimb tracks change.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]


def repair_roar():
    scene = bpy.context.scene
    rig = bpy.data.objects['BrunoRig']
    rig.data.pose_position = 'POSE'
    action = bpy.data.actions['Bruno_Roar']
    source = bpy.data.actions.get('SOURCE_Roar_PreForelimbFix')
    if source is None:
        source = action.copy()
        source.name = 'SOURCE_Roar_PreForelimbFix'
        source.use_fake_user = True
    helpers = (ROOT / 'tools/blender/circus_bear_motion.py').read_text().split('for title,count in')[0]
    env = dict(rig=rig, scene=scene, bpy=bpy, math=math, Vector=Vector, Matrix=Matrix, Quaternion=Quaternion)
    exec(compile(helpers, 'bruno_ik_helpers', 'exec'), env)
    solve, smooth = env['leg'], env['smooth']
    previous = {}
    lowest_elbow_drop = 100
    for frame in range(1, 98):
        rig.animation_data.action = source
        scene.frame_set(frame)
        t = (frame - 1) / 96
        rise = smooth(t / .27) * (1 - smooth((t - .72) / .28))
        for side, sign in [('L', 1), ('R', -1)]:
            upper = rig.pose.bones['ForeUpper.' + side]
            # Follow the shoulder throughout the rise; the old world-space
            # target went above it and inverted the elbow near half height.
            rest_offset = rig.data.bones['ForePaw.' + side].head_local - upper.bone.head_local
            offset = rest_offset.lerp(Vector((sign * .09, .51, -.40)), rise)
            # A restrained, staggered movement in the held pose, not a mirror.
            hold = smooth((t - .27) / .10) * (1 - smooth((t - .61) / .11))
            offset.y += .025 * math.sin(t * math.tau * 1.5 + sign * .7) * hold
            offset.z += .035 * math.sin(t * math.tau + sign * .65) * hold
            pole = Vector((0, -1, 0)).lerp(Vector((sign * .12, -.4, -1)), rise)
            solve('Fore', side, upper.head + offset, roll=-.35 * rise, pole_hint=pole)
            rig.pose.bones['ForeToes.' + side].rotation_quaternion = Quaternion((1, 0, 0), .06 * rise)
            if rise > .99:
                lowest_elbow_drop = min(lowest_elbow_drop, upper.head.z - rig.pose.bones['ForeLower.' + side].head.z)
        rig.animation_data.action = action
        for bone in rig.pose.bones:
            if not bone.name.startswith(('ForeUpper.', 'ForeLower.', 'ForePaw.', 'ForeToes.')):
                continue
            if bone.name in previous and bone.rotation_quaternion.dot(previous[bone.name]) < 0:
                bone.rotation_quaternion.negate()
            previous[bone.name] = bone.rotation_quaternion.copy()
            for prop in ('rotation_quaternion', 'location', 'scale'):
                bone.keyframe_insert(prop, frame=frame)
    action['forelimb_fix'] = 'Shoulder-relative reach, downward elbow pole, relaxed wrists, asymmetric hold'
    scene.frame_set(43)
    print('REAR_FIXED', 'minimum elbow below shoulder:', round(lowest_elbow_drop, 4))


if __name__ == '__main__':
    repair_roar()
