"""Additional original pavilion spectators; runs through Blender MCP in its own scene."""
import bpy, math, json, ast
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/HoleInWallStudio'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/HoleInWallStudio'
previous=bpy.context.window.scene
scene=bpy.data.scenes.new('HoleInWall_AudienceDetail')
bpy.context.window.scene=scene
materials={e['name'][3:]:bpy.data.materials[e['name']] for e in json.loads((ART/'palette.json').read_text())['materials']}
parts=[]; exports=[]; export_models=None
# Reuse the established mesh/export functions without running the arena generator.
tree=ast.parse((ROOT/'tools/blender/hole_in_wall_studio.py').read_text())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ('finish','box','sphere','tube','export'):
        exec(compile(ast.Module(body=[node],type_ignores=[]),'<studio helpers>','exec'))
for variant in range(3,7):
    for cheer in (False,True):
        shirt=('Coral','Teal','Ivory')[variant%3]
        seated=variant==6
        dz=-.30 if seated else 0
        for x in (-.14,.14):
            footY=-.36 if seated else -.035
            box((x,footY,.09),(.20,.36,.16),'Ink',.035)
            knee=(x,-.36,.42) if seated else (x,0,.42)
            tube((x,footY,.16),knee,.10,'Blue')
            tube(knee,(x,0,.70+dz),.105,'Blue')
        sphere((0,0,1+dz),(.32,.18,.38),shirt)
        tube((0,0,1.28+dz),(0,0,1.41+dz),.085,'Skin')
        sphere((0,-.015,1.55+dz),(.19,.16,.23),'Skin')
        sphere((0,.035,1.68+dz),(.197,.16,.13),'Hair')
        for x in (-.066,.066):
            sphere((x,-.165,1.58+dz),(.035,.018,.039),'White')
            sphere((x,-.181,1.58+dz),(.013,.009,.021),'Ink')
        sphere((0,-.181,1.51+dz),(.035,.035,.045),'Skin')
        box((0,-.163,1.445+dz),(.08,.02,.023),'Hair',.005)
        for s in (-1,1):
            shoulder=(s*.27,0,1.20+dz)
            if cheer:
                elbow=(s*.43,-.03,1.51+dz); wrist=(s*.49,-.06,1.90+dz)
            elif variant==3: # hands meeting in front, applause
                elbow=(s*.39,-.16,1.02); wrist=(s*.07,-.36,1.22)
            elif variant==4 and s==1: # one arm waving
                elbow=(.43,-.04,1.46); wrist=(.54,-.10,1.84)
            elif variant==5: # leaning forearms on knees / rail
                elbow=(s*.34,-.23,.90); wrist=(s*.19,-.39,.94)
            elif seated:
                elbow=(s*.34,-.10,.91+dz); wrist=(s*.20,-.37,.75+dz)
            else:
                elbow=(s*.43,-.03,.91); wrist=(s*.49,-.06,.70)
            tube(shoulder,elbow,.088,shirt);tube(elbow,wrist,.062,'Skin');sphere(wrist,(.082,.063,.09),'Skin')
        export('Fan%d_%s'%(variant,'Cheer' if cheer else 'Idle'))
bpy.data.libraries.write(str(SOURCE/'HoleInWallAudienceDetail.blend'),{scene},fake_user=True)
bpy.context.window.scene=previous
print(json.dumps(exports))
