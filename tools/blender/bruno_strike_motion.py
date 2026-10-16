"""Re-author only Bruno's striking foreleg, with an elbow below the shoulder."""
import bpy
import math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion


def apply():
    scene = bpy.context.scene
    rig = bpy.data.objects['BrunoRig']
    action = bpy.data.actions['Bruno_Strike']
    source = bpy.data.actions.get('SOURCE_Strike_PreForelimbFix')
    if source is None:
        source = action.copy()
        source.name = 'SOURCE_Strike_PreForelimbFix'
        source.use_fake_user = True
    helpers = (Path(__file__).with_name('circus_bear_motion.py')).read_text().split('for title,count in')[0]
    env = dict(rig=rig, scene=scene, bpy=bpy, math=math, Vector=Vector, Matrix=Matrix, Quaternion=Quaternion)
    exec(compile(helpers, 'bruno_strike_ik', 'exec'), env)
    solve, smooth = env['leg'], env['smooth']
    previous = {}
    for frame in range(1, 44):
        rig.animation_data.action = source
        scene.frame_set(frame)
        seconds = (frame - 1) / 30
        wind = smooth(seconds / .19)
        swing = smooth((seconds - .19) / .19)
        follow = smooth((seconds - .38) / .22)
        settle = smooth((seconds - .72) / .62)
        weight = wind * (1 - settle)
        upper = rig.pose.bones['ForeUpper.R']
        original = rig.pose.bones['ForePaw.R'].head - upper.head
        offset = Vector((-.12, .55, -.18)).lerp(Vector((.33, .70, -.12)), swing)
        offset = offset.lerp(Vector((.50, .50, -.30)), follow)
        offset = original.lerp(offset, weight)
        pole = Vector((0, -1, 0)).lerp(Vector((-.15, -.5, -1)), weight)
        solve('Fore', 'R', upper.head + offset, roll=-.15 * weight, pole_hint=pole)
        rig.animation_data.action = action
        for name in ('ForeUpper.R', 'ForeLower.R', 'ForePaw.R'):
            bone = rig.pose.bones[name]
            if name in previous and bone.rotation_quaternion.dot(previous[name]) < 0:
                bone.rotation_quaternion.negate()
            previous[name] = bone.rotation_quaternion.copy()
            for prop in ('rotation_quaternion', 'location', 'scale'):
                bone.keyframe_insert(prop, frame=frame)
    action['strike_fix'] = 'Shoulder-relative low wind-up, downward elbow, restrained wrist'
    scene.frame_set(7)
    print('Strike foreleg repaired; body/support timing and other clips preserved.')


if __name__ == '__main__':
    apply()
