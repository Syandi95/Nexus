# Zoom/Pan/Clip Parity Matrix: PowerShell vs C#

| # | Behavior | PowerShell | C# | Status |
|---|----------|-----------|----|--------|
| 1 | **Clip/thumb reset BEFORE new image load** | `clip.Rect=Empty`, `thumb.RenderTransform=Identity`, `stage.RenderTransform=Identity` before `before.Source=$bitmap` (lines 694-697) | Not reset | ❌ **Bug A1** |
| 2 | **Geometry sync at Render priority after load** | `Invoke-RemoveBackgroundPreviewGeometrySync` dispatches `DispatcherPriority::Render` (line 709 → line 852) | Synchronous via ZoomLevel PropertyChanged | ❌ **Bug A2** |
| 3 | **Geometry sync after image decoded** | N/A (OnLoad bitmap decoded synchronously, then Render dispatch) | Missing `ImageOpened` handler | ❌ **Bug A3** |
| 4 | **DragDelta uses rendered (zoomed) width** | Falls back to ActualWidth, then tries `TransformToAncestor` for rendered width (lines 1392-1398) | Uses only `ActualWidth` | ❌ **Bug B** |
| 5 | **Wheel zoom guard (not over thumb)** | `Test-RemoveBackgroundCompareInput` walks visual tree (line 827-844) | No guard | ❌ **Bug C1** |
| 6 | **Pan start guard (not over thumb)** | Same `Test-RemoveBackgroundCompareInput` guard (line 827) | No guard | ❌ **Bug C2** |
| 7 | **Pan reset on image load** | panX=0, panY=0, isPanning=false (lines 682-684) | Via ZoomLevel=100 → PropertyChanged → reset (works) | ✅ |
| 8 | **ToggleCompare sets ratio to 0.5** | `$compareRatio=0.5`, then `Update-RemoveBackgroundCompareClip` (line 1378) | `CompareRatio=0.5` → PropertyChanged → `SyncPreviewGeometry` | ✅ |
| 9 | **Result state → explicit clip calc** | `Set-RemoveBackgroundState('Result')` calls `Update-RemoveBackgroundCompareClip` (line 442) | `HasResult=true`, `CompareRatio=0.5` → PropertyChanged → `SyncPreviewGeometry` | ✅ |
| 10 | **Before image visibility** | Load: `Visible`; Result: `Visible` (lines 697, 1379) | Always visible when `IsImageLoaded` (XAML DataTrigger) | ✅ |
| 11 | **After image visibility** | Load: `Collapsed`; Result: `Visible` (lines 698, 1380) | Shown when `ResultImageSource != null` | ✅ |
| 12 | **Compare thumb visibility** | Result: `Visible`, else `Collapsed` (line 436) | Bound to `IsCompareActive` | ✅ |
| 13 | **Wheel zoom center-on-cursor** | Same formula (lines 818-822) | Same formula (lines 253-258) | ✅ |
| 14 | **Clip rect formula** | `Rect(clipX, 0, w*(1-r), h)` where w=ActualWidth (lines 868-873) | Same formula (line 163) | ✅ |
| 15 | **Thumb position via TransformToAncestor** | Uses `TransformToAncestor` → rendered position, clamped (lines 878-886) | Same logic (lines 168-175) | ✅ |

## Root Cause Hypothesis

"The root cause of zoom/pan/clip bugs is that C# `LoadImage` does not reset clip/thumb geometry before the new image is assigned, and geometry sync happens synchronously via `ZoomLevel=100` PropertyChanged — before WPF has decoded the new bitmap and updated layout. PowerShell avoids this by resetting clip/thumb geometry to Empty/Identity before assigning the new image, and by dispatching geometry sync at `DispatcherPriority.Render` after every state transition that affects image dimensions."

Secondary: "The compare thumb DragDelta handler divides by `ActualWidth` (unstretched layout width) instead of rendered width from `TransformToAncestor`, causing the slider to move zoom-times faster than expected when zoomed in."
