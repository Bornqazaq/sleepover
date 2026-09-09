# Local Blender control

Verified on Blender 5.2.0 LTS with the existing `addon` (Blender MCP).
The bridge listens on localhost:9876. No external generation service is needed
for direct scene editing. External AI generation services require separate setup.

Launch from the repository root when Blender is closed:

```sh
open -a Blender --args --python "$PWD/tools/blender_start.py"
```

If Blender is already open, use its BlenderMCP sidebar to start the server;
macOS may ignore launch arguments for an already running application.
The server must be started again after quitting Blender.

```sh
python3 tools/blender_client.py get_scene_info
python3 tools/blender_client.py execute_code --file /tmp/model.py
python3 tools/blender_client.py get_viewport_screenshot --params '{"filepath":"/tmp/blender-preview.png","max_size":1200}'
```

The client executes Blender Python in the active scene. Inspect the scene before
editing; save intentional source assets into `igruha/Assets/_Project/Art/Source/`.
Use reference images to guide geometry, materials and composition, then inspect
viewport images/renders before exporting game assets. FBX and glTF export
operators are available; actual export settings should be checked per asset.

Connection verification: scene read, temporary mesh creation/removal, and
offscreen viewport capture all succeeded. No existing project asset was changed.
