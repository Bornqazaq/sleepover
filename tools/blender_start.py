"""Run with Blender --python to enable the installed local MCP bridge."""
import bpy
import addon_utils


def start():
    addon_utils.enable("addon", default_set=False)
    bpy.context.scene.blendermcp_port = 9876
    bpy.ops.blendermcp.start_server()
    print("Sleepover Blender bridge ready on localhost:9876")
    return None


bpy.app.timers.register(start, first_interval=1.0)
