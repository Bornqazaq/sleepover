# Impulse Synchronization via ServerRpc (IGR-53)

## Architecture: Server-Authoritative Impulse

```
┌──────────────────┐              ┌──────────────────┐
│ Client A         │              │ Client B         │
│ (Owner: HOST)    │              │ (Owner: CLIENT)  │
│                  │              │                  │
│ PlayerController │              │ PlayerController │
│ Collision!       │              │ Collision!       │
│                  │              │                  │
│ ApplyPush()  ←────────┐     ┌────→ ApplyPush()     │
│ (local)          │    │     │    │ (local)         │
│                  │    │     │    │                 │
└──────────────────┘    │     │    └──────────────────┘
                        │     │
                  ┌─────┴──┬──┴─────┐
                  │ Both call RPC   │
                  └─────┬──┬────────┘
                        │  │
                        │  └──→ OnCollisionEnter
                        │        → NetworkApplyPush()
                        │        → ApplyPushServerRpc()
                        │
                        ↓
            ┌──────────────────────┐
            │ HOST (Server)        │
            │                      │
            │ ApplyPushServerRpc() │
            │ {                    │
            │   validated = true;  │
            │   push applied;      │
            │   Rigidbody updated  │
            │ }                    │
            │                      │
            └──────────┬───────────┘
                       │
            ┌──────────┴──────────┐
            │ NetworkTransform    │
            │ replicates new      │
            │ position/velocity   │
            │ to ALL clients      │
            └──────────┬──────────┘
                       │
        ┌──────────────┴──────────────┐
        │                             │
        ↓                             ↓
    ┌─────────┐               ┌──────────┐
    │ Client A│               │ Client B │
    │ sees B  │               │ sees A   │
    │ pushed  │               │ pushed   │
    └─────────┘               └──────────┘
```

## Two Types of Impulse

### 1. ApplyPush (collision-aware)
```csharp
public void ApplyPush(Vector3 direction, float force)
{
    // Calculates:
    // - Is it a face hit or back hit?
    // - Apply different force multipliers
    // - Apply upward component
    // - Trigger knockdown animation
}

// Called when:
// - OnCollisionEnter detects horizontal/vertical impact
// - Another player bumps into you
// - Environmental object hits you
```

**Flow:**
```
OnCollisionEnter() → Calculate direction/force
                  → NetworkApplyPush(direction, force)
                  → ApplyPushServerRpc() on server
                  → Rigidbody.AddForce applied
                  → NetworkTransform replicates velocity
                  → All see the push
```

### 2. ApplyImpulse (arbitrary direction)
```csharp
public void ApplyImpulse(Vector3 impulse)
{
    // Direct impulse without face/back detection
    // Used for:
    // - Spring platforms
    // - Explosions
    // - Traps with arbitrary directions
}

// Called when:
// - Spring trap triggers
// - Explosive object hits
// - Environmental hazard activates
```

## Implementation Details

### ServerRpc Signature
```csharp
[ServerRpc(RequireOwnershipck = true)]
private void ApplyPushServerRpc(Vector3 direction, float force)
{
    // Only owner can call this RPC
    // Server validates and applies
    // Result replicates via NetworkTransform
}
```

**Key points:**
- `[ServerRpc]` — only server can execute this method
- `RequireOwnershipck = true` — only owner of NetworkObject can call it
- Server then replicates result through NetworkTransform
- Other RPC types not needed — physics result flows through transform

## When Collisions Happen

```csharp
// In PlayerController.OnCollisionEnter():
private void OnCollisionEnter(Collision collision)
{
    if (config == null || IsKnockedDown)
        return;
        
    // Calculate impact
    float deltaV = collision.impulse.magnitude / rb.mass;
    
    // If strong enough → trigger knockdown
    if (deltaV >= config.KnockdownVelocityThreshold)
    {
        // For networked version:
        // Get the NetworkPlayerController
        var netCtrl = GetComponent<NetworkPlayerController>();
        
        // Send impulse to server
        netCtrl.NetworkApplyPush(direction, force);
        
        // Server applies and replicates
    }
}
```

## Validation on Server

The server MUST validate impulses to prevent cheating:

```csharp
// Server-side checks (future enhancements):
bool isValidPush = true;

// Check 1: Is the pusher actually close?
if (Vector3.Distance(pusher.position, target.position) > maxReachDistance)
    isValidPush = false;
    
// Check 2: Is the force within reasonable bounds?
if (force < 0f || force > maxForce)
    isValidPush = false;
    
// Check 3: Is the target knockeddown already?
if (target.IsKnockedDown)
    isValidPush = false;

if (!isValidPush)
    return; // Reject
```

---

## Testing Impulse Sync

### Local Test (single window)
```
1. Open Boot scene, Play
2. Two capsule characters spawn
3. Push one into the other (if physics allows)
4. Both should react to collision
```

### Network Test (Host + Client)
```
1. Run Host (Play in Editor)
2. Run Client (--client arg)
3. Both see two characters

Test case A: Direct collision
  - In Host window: Move character into other
  - Expected: Both see knockdown animation
  - Check: Client sees Host character pushed
  - Check: Host sees Client character pushed

Test case B: One character falls and pushes other
  - In Host window: Jump on Client's character
  - Expected: Client's character knocked down
  - Check: Animation synced
  - Check: Velocity change synchronized
```

---

## Flow Summary

```
┌─────────────────────────────────────────┐
│ Collision detected (OnCollisionEnter)    │
├─────────────────────────────────────────┤
│ Calculate: direction, force, type       │
├─────────────────────────────────────────┤
│ NetworkApplyPush(dir, force)            │
│   → ApplyPushServerRpc(dir, force)      │
├─────────────────────────────────────────┤
│ [SERVER side]                           │
│ ✅ Validate impulse values              │
│ ✅ Apply Rigidbody.AddForce()           │
│ ✅ Check if knockdown threshold met     │
│ ✅ Trigger KnockdownStarted event      │
├─────────────────────────────────────────┤
│ NetworkTransform serializes:            │
│ - linearVelocity (changed by AddForce)  │
│ - angularVelocity (if spinning)         │
│ - rotation (if rotated)                 │
├─────────────────────────────────────────┤
│ [CLIENT side]                           │
│ ✅ Receive NetworkTransform update      │
│ ✅ Apply new velocity to Rigidbody      │
│ ✅ See character move/knockdown         │
└─────────────────────────────────────────┘
```

---

## Known Issues & Fixes

### "RPC call failed" error
- **Fix:** Verify `RequireOwnershipck = true` is spelled correctly
- **Fix:** Check that the player calling RPC is the owner (IsOwner == true)

### Impulse doesn't propagate
- **Fix:** Verify ServerRpc is called (check for errors)
- **Fix:** Verify NetworkTransform is on the same GameObject
- **Fix:** Check that Rigidbody.isKinematic != true (should be false)

### Impulse applied twice (too strong)
- **Fix:** Don't call both local ApplyPush and ServerRpc
- **Fix:** Only use NetworkApplyPush → ServerRpc
- **Fix:** Never call Rigidbody.AddForce directly from client

### Animation doesn't play
- **Fix:** Ensure NetworkCharacterAnimatorDriver is set up (IGR-52)
- **Fix:** Verify KnockdownStarted event fires

---

## Next Steps

- **IGR-54:** Full network playtest (two players moving + colliding)
- **IGR-265:** Connection Approval validation
- **IGR-55:** Relay integration for remote players

