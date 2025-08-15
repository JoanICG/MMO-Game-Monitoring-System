# Camera System Setup Guide

## Quick Setup (5 minutes)

### 1. Setup Main Camera
1. Select your **Main Camera** in the scene
2. Add these components:
   - `CameraManager` (drag script)
   - `ThirdPersonCamera` (drag script)  
   - `FreeCamera` (drag script)

### 2. Configure ThirdPersonCamera
- **Target**: Drag your player GameObject here
- **Offset**: Try (0, 5, -8) for behind-the-player view
- **Mouse Sensitivity**: Start with 2.0
- **Distance Limits**: Min=3, Max=15
- **Collision Layers**: Set to environment/walls

### 3. Configure CameraManager
- **Default Mode**: ThirdPerson
- **Switch Camera Key**: C (or your preference)

## Controls

### Third Person Camera
- **Right Click + Mouse**: Rotate camera around player
- **Mouse Scroll**: Zoom in/out
- **ESC**: Toggle cursor lock

### Free Camera  
- **F**: Toggle free camera mode
- **WASD**: Move camera
- **QE**: Up/Down movement
- **Right Click + Mouse**: Look around
- **Shift**: Fast movement
- **Ctrl**: Slow movement

### Camera Switching
- **C**: Cycle through camera modes
- **1**: Third Person Camera
- **2**: Free Camera

## Advanced Configuration

### Third Person Camera Settings
```csharp
// In ThirdPersonCamera component
[Header("Camera Controls")]
public float mouseSensitivity = 2f;      // How fast camera rotates
public float scrollSensitivity = 2f;     // Zoom speed
public bool invertY = false;             // Invert vertical look

[Header("Camera Limits")]
public float minDistance = 3f;           // Closest zoom
public float maxDistance = 15f;          // Farthest zoom
public float minVerticalAngle = -30f;    // Look down limit
public float maxVerticalAngle = 60f;     // Look up limit

[Header("Smoothing")]
public float positionSmoothTime = 0.3f;  // Position smoothing
public float rotationSmoothTime = 0.1f;  // Rotation smoothing

[Header("Collision")]
public LayerMask collisionLayers = 1;    // What blocks camera
public float collisionRadius = 0.3f;     // Camera collision size
```

### Free Camera Settings
```csharp
[Header("Movement")]
public float normalSpeed = 10f;          // Normal movement speed
public float fastSpeed = 20f;            // Shift key speed
public float slowSpeed = 2f;             // Ctrl key speed

[Header("Look")]
public float mouseSensitivity = 2f;      // Mouse look sensitivity
public bool invertY = false;             // Invert vertical look
```

## Common MMO Camera Patterns

### 1. Classic MMO Setup
- **Distance**: 8-12 units behind player
- **Height**: 3-5 units above player
- **Angle**: Slightly looking down
- **Collision**: Enabled for walls/terrain

### 2. Action MMO Setup  
- **Distance**: 5-8 units behind player
- **Height**: 2-3 units above player
- **Angle**: More horizontal view
- **Fast Response**: Lower smoothing values

### 3. Strategic MMO Setup
- **Distance**: 10-15 units behind player
- **Height**: 5-8 units above player
- **Angle**: Top-down perspective
- **Wide View**: Higher max distance

## Script Integration

### Auto-Find Player
The CameraManager automatically finds your LocalPlayerController:
```csharp
var playerController = FindObjectOfType<LocalPlayerController>();
if (playerController != null)
{
    SetPlayerTarget(playerController.transform);
}
```

### Manual Player Assignment
```csharp
// In your game manager or player spawn code
CameraManager camManager = FindObjectOfType<CameraManager>();
camManager.SetPlayerTarget(newPlayerTransform);
```

### Camera Mode Switching via Code
```csharp
CameraManager camManager = FindObjectOfType<CameraManager>();

// Switch to specific mode
camManager.SetCameraMode(CameraManager.CameraMode.ThirdPerson);
camManager.SetCameraMode(CameraManager.CameraMode.Free);

// Cycle through modes
camManager.SwitchToNextCamera();

// Check current mode
if (camManager.GetCurrentMode() == CameraManager.CameraMode.Free)
{
    // Do something when in free camera mode
}
```

## Troubleshooting

### Camera Won't Follow Player
1. Check if **Target** is assigned in ThirdPersonCamera
2. Verify **LocalPlayerController** exists in scene
3. Make sure **CameraManager** is enabled

### Camera Goes Through Walls
1. Set **Collision Layers** to include walls/terrain
2. Adjust **Collision Radius** (try 0.5-1.0)
3. Check if walls have colliders

### Camera Too Jerky/Smooth
- **Too Jerky**: Increase smoothing values (0.3-0.5)
- **Too Smooth**: Decrease smoothing values (0.05-0.1)

### Controls Not Working
1. Check **Input Manager** settings (Edit > Project Settings > Input Manager)
2. Verify **Mouse X**, **Mouse Y**, **Mouse ScrollWheel** exist
3. Make sure camera scripts are **enabled**

## Performance Tips

1. **Use FixedUpdate** for camera physics
2. **Limit raycast frequency** for collision detection  
3. **Pool camera calculations** for multiple players
4. **Cull distant objects** based on camera distance

## Future Enhancements

- **First Person Camera**: For close combat
- **Cinematic Camera**: For cutscenes/death cam
- **Spectator Camera**: For watching other players
- **Orbital Camera**: For character customization
- **Shake Effects**: For impact feedback
