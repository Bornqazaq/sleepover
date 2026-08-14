# Animation Synchronization Setup (IGR-52)

## What to do manually in Unity Editor

### Step 1: Add NetworkAnimator to Player.prefab
```
1. Open Assets/_Project/Prefabs/Player/Player.prefab
2. Inspector → Add Component
3. Search "NetworkAnimator"
4. Add Unity.Netcode.NetworkAnimator
5. Set Animator reference (should auto-find)
```

### Step 2: Configure NetworkAnimator
```
NetworkAnimator settings:
├─ Animator: [drag Animator component from child]
├─ Client Authoritative: ❌ OFF (use server-authoritative)
│  (Because owner's client controls animation, not server)
└─ Parameters to sync:
   ├─ Speed (float) — NormalizedSpeed from PlayerController
   ├─ Jump (trigger) — Jumped event
   ├─ KnockdownFront (trigger) — Knockdown event (front)
   └─ KnockdownBack (trigger) — Knockdown event (back)
```

### Step 3: Replace CharacterAnimatorDriver with NetworkCharacterAnimatorDriver
```
1. On Player.prefab:
   - Remove or disable CharacterAnimatorDriver component
   - Add Component → NetworkCharacterAnimatorDriver
   
2. Set AnimatorDriver reference (should auto-find CharacterAnimatorDriver)
```

### Step 4: Verify Animator Controller has correct parameters
```
Open Animator window for Player model
Verify these parameters exist:
✅ Speed (float) — idle/walk/run states use this
✅ Jump (trigger) — Jump state
✅ KnockdownFront (trigger) — FallForward animation
✅ KnockdownBack (trigger) — FlyBack animation
✅ Punch (trigger) — (used by PlayerPushAbility, if present)
```

---

## How Animation Sync Works

```
┌────────────────────────────────┐
│ Owner's Client (IsOwner=true) │
└──────────────┬─────────────────┘
               │
         PlayerController.Update()
               │
         ┌─────┴──────────┐
         │ SetFloat(Speed)│
         │ SetTrigger(...) │
         └─────┬──────────┘
               │
         animator.SetFloat() ←── local Animator
         animator.SetTrigger()   (NOT networked yet)
               │
         NetworkAnimator.OnAnimatorIK()
         (serializes all parameter changes)
               │
         ┌──────────────────────┐
         │ Send over network    │
         └──────┬───────────────┘
                │
    ┌───────────┴────────────┐
    │                        │
    ↓                        ↓
┌─────────────┐        ┌──────────────┐
│ Other       │        │ Server logs  │
│ Clients     │        │ everything   │
└─────┬───────┘        └──────────────┘
      │
      ├─ Receive animator parameter sync
      │
      ├─ Apply to LOCAL animator
      │  (SetFloat/SetTrigger locally)
      │
      └─ Animator plays correct anim
         (Jump, Knockdown, Walk, etc.)
```

---

## What's already done ✅

- ✅ CharacterAnimatorDriver.cs exists and works (uses events from PlayerController)
- ✅ NetworkCharacterAnimatorDriver.cs created — extends with NetworkAnimator support
- ✅ PlayerController fires Jumped and KnockdownStarted events
- ✅ Animator parameters hashed with StringToHash (efficient)

## Manual steps still required

- 🔧 Add NetworkAnimator component to Player.prefab (in Editor)
- 🔧 Configure which parameters to sync (Speed, Jump, Knockdown triggers)
- 🔧 Replace CharacterAnimatorDriver with NetworkCharacterAnimatorDriver
- 🔧 Verify Animator Controller has all required parameters

---

## Testing Animation Sync

### Single player test (local):
```
1. Play Boot scene
2. Move WASD → Speed parameter updates → idle/walk animation plays
3. Press Space → Jump trigger → Jump animation plays
4. Run into wall → Knockdown trigger → Fall animation plays
```

### Network test (Host + Client):
```
1. Run Host (Boot scene in Editor with Play)
2. Run Client (second window with --client arg)

Expected:
  ✅ In HOST window:
     - Press W → animator shows "walk"
     - Console: "🎬 [Player_0] Владелец персонажа"
     
  ✅ In CLIENT window:
     - See HOST's player walking
     - Animations synchronized in real-time
     - No jerky/broken animations
     - Speed parameter smooth (not binary idle/walk)
```

---

## Known Issues

### "Animator parameter 'Speed' not found"
- ✅ Fix: Verify Animator Controller has "Speed" float parameter
- ✅ Check: Animator is on child GameObject with correct hierarchy

### Animations don't sync across network
- ✅ Fix 1: Add NetworkAnimator component to Player.prefab
- ✅ Fix 2: Verify NetworkAnimator points to correct Animator
- ✅ Fix 3: Check that NetworkCharacterAnimatorDriver is enabled (IsOwner check)

### Animations jittery / teleport between frames
- ✅ Fix: Animator blending might be too fast. Check animation transition timing.

---

## Next Steps

After IGR-52 (Animation Sync):
- **IGR-53**: Synced impulse/push physics through ServerRpc
- **IGR-54**: Full playtest with movement + animation + impulse

