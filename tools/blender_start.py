"""Run with Blender --python to enable the installed local MCP bridge."""
import bpy
import addon_utils


def start():
    available = {module.__name__ for module in addon_utils.modules()}
    module_name = next(
        (name for name in ("blender_mcp", "addon") if name in available), None
    )
    if module_name is None:
        raise RuntimeError("Blender MCP addon is not installed")
    addon_utils.enable(module_name, default_set=False)
    bpy.context.scene.blendermcp_port = 9876
    bpy.ops.blendermcp.start_server()
    print("Sleepover Blender bridge ready on localhost:9876")
    return None


bpy.app.timers.register(start, first_interval=1.0)
