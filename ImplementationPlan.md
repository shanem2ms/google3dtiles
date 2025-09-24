# Implementation Plan: 3D Tiles Optimization

This document outlines the steps required to implement the "Neighbor-Based Traversal" and "Session Persistence" features as described in `Google3DTilesRendering.md`.

---

## Part 1: Implementing Neighbor-Based Traversal and Caching

This change refactors the core rendering loop from a top-down traversal to a fringe-based traversal, significantly reducing per-frame processing.

### Step 1.1: Data Structure Modifications

**File:** `cs/Tile.cs`

1.  **Add Caching Property:** Add a property to track when a tile was last part of the visible set. This will be used for cache eviction.
    ```csharp
    public int LastVisibleFrame { get; set; } = 0;
    ```
2.  **Neighbor Properties (Future):** While full neighbor-finding is complex, we can lay the groundwork. For now, this is a placeholder. A more advanced implementation would require a spatial partitioning system.
    ```csharp
    // public List<Tile> Neighbors { get; set; } = new List<Tile>();
    ```

### Step 1.2: Core Logic Refactoring

**File:** `cs/MainWindow.xaml.cs`

1.  **Introduce New State Variables:**
    *   Replace `Tile root;` with the following to manage the new traversal logic:
        ```csharp
        private Tile _root;
        private HashSet<Tile> _fringeTiles = new HashSet<Tile>();
        private Dictionary<string, Tile> _tileCache = new Dictionary<string, Tile>();
        ```

2.  **Modify `MainWindow` Constructor:**
    *   The callback from `GoogleTile.CreateFromUri` should now initialize the new state variables.
        ```csharp
        // Inside the ContinueWith block
        _root = new Tile(rootTile.root, null);
        _tileCache.Add("root", _root); // Add root to the cache
        _fringeTiles.Add(_root);      // The initial fringe is just the root
        RefreshTiles(); // This method will also need to be updated
        veldridRenderer.OnRender = OnRender;
        ```

3.  **Overhaul the `OnRender` Method:**
    *   This is the most significant change. The existing `root.DownloadChildren` call will be replaced with a new fringe-based traversal logic.

    ```csharp
    void OnRender(CommandList _cl, GraphicsDevice _gd, Swapchain _sc)
    {
        cameraView?.Update();
        frameIdx++;

        if (DownloadEnabled)
        {
            // The new traversal logic will be managed here
            UpdateTilesBasedOnFringe();
        }

        // ... (rest of the rendering logic remains the same) ...
        
        if (earthViz != null)
            earthViz.Draw(_cl, cameraView, _root, frameIdx); // Still draw from the root
        // ...
    }
    ```

4.  **Implement `UpdateTilesBasedOnFringe()`:**
    *   This new method will contain the core traversal logic. It uses a queue to process tiles, allowing it to traverse up and down the tree within a single frame to handle panning and zooming gracefully.

    **Note on Panning and Neighbor Discovery:** This approach correctly handles panning and discovery of adjacent tiles, even those whose common ancestor is a grandparent. If a tile moves out of view, its parent is immediately added to a queue for processing *in the same frame*. The logic can then traverse up the tree multiple levels (e.g., to a grandparent) and then back down a different branch to find the new, incoming tiles, all within one frame update. This prevents the "pop-in" effect that would occur with a more naive, multi-frame approach.

    ```csharp
    private async void UpdateTilesBasedOnFringe()
    {
        HashSet<Tile> nextFringe = new HashSet<Tile>();
        List<Task> downloadTasks = new List<Task>();
        
        // Tiles to process in the current frame. Starts with the last frame's fringe.
        Queue<Tile> processingQueue = new Queue<Tile>(_fringeTiles);
        
        // Keep track of tiles we've already processed this frame to avoid cycles or redundant work.
        HashSet<Tile> processedThisFrame = new HashSet<Tile>();

        while (processingQueue.Count > 0)
        {
            Tile tile = processingQueue.Dequeue();
            if (!processedThisFrame.Add(tile))
            {
                continue; // Already processed this tile in this frame's loop
            }

            // If a tile is not in view, we immediately queue its parent for processing in this same frame.
            // This allows us to traverse up the tree and find sibling branches (cousins) much faster.
            if (!tile.Bounds.IsInView(cameraView))
            {
                if (tile.Parent != null && !processedThisFrame.Contains(tile.Parent)) {
                    processingQueue.Enqueue(tile.Parent);
                }
                continue;
            }

            tile.LastVisibleFrame = frameIdx;
            float error = tile.GetGeometricError(cameraView);

            // 1. Coarsening Check: If the tile is too detailed for the current view, add its parent to the next frame's fringe.
            if (error < COARSEN_THRESHOLD && tile.Parent != null)
            {
                nextFringe.Add(tile.Parent);
            }
            // 2. Refinement Check: If the tile is not detailed enough, move down to its children.
            else if (error > REFINE_THRESHOLD)
            {
                if (tile.ChildTiles != null)
                {
                    foreach (var child in tile.ChildTiles)
                    {
                        // Add children to be processed in the *current* frame's loop.
                        if (!processedThisFrame.Contains(child)) {
                            processingQueue.Enqueue(child);
                        }
                    }
                }
                else if (tile.ChildJson != null && !tile.IsDownloading)
                {
                    downloadTasks.Add(tile.DownloadChildren(sessionkey, cameraView, frameIdx, false));
                    nextFringe.Add(tile); // Keep this tile in the fringe until children are loaded.
                }
                else
                {
                    nextFringe.Add(tile); // Leaf node, stays in the fringe.
                }
            }
            // 3. No Change: LOD is appropriate, tile stays in the fringe.
            else
            {
                nextFringe.Add(tile);
            }
        }

        _fringeTiles = nextFringe;
        if(downloadTasks.Any())
        {
            await Task.WhenAll(downloadTasks);
        }
    }
    ```

### Step 1.3: `Tile.cs` Modifications

1.  **Add `GetGeometricError()` Method:**
    *   This centralizes the error calculation.
    ```csharp
    public float GetGeometricError(CameraView cv)
    {
        if (Bounds.IsInside(cv)) return float.PositiveInfinity; // Always refine if inside

        float dist = MathF.Sqrt(Bounds.DistanceSqFromPt(cv.Pos));
        if (GeometricError == 0) return float.PositiveInfinity; // Represents a leaf node that can't be refined

        return (dist / GeometricError);
    }
    ```
2.  **Modify `DownloadChildren()`:**
    *   This method needs to be adapted to fit the new caching model. It should now add new tiles to the main `_tileCache` and handle child assignment. It should also be made non-recursive.
    *   The method signature should change to accept the cache.
    *   It should set an `IsDownloading` flag to prevent redundant requests.

---

## Part 2: Implementing Session Persistence

This feature will save the application's state on exit and restore it on startup.

### Step 2.1: Create `SessionState` Class

**New File:** `cs/SessionState.cs`

```csharp
using System.Collections.Generic;
using System.Numerics;

public class SessionState
{
    public Vector3 CameraPosition { get; set; }
    public Quaternion CameraRotation { get; set; }
    public List<string> FringeTileUris { get; set; }
    public Dictionary<string, string> TileHierarchy { get; set; } // Key: Child URI, Value: Parent URI
}
```

### Step 2.2: Implement Save Logic

**File:** `cs/MainWindow.xaml.cs`

1.  **Add `OnClosing` Event Handler:**
    ```csharp
    public MainWindow()
    {
        // ... existing constructor logic ...
        this.Closing += OnMainWindowClosing;
    }

    private void OnMainWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSessionState();
    }
    ```

2.  **Implement `SaveSessionState()`:**
    ```csharp
    private void SaveSessionState()
    {
        var state = new SessionState
        {
            CameraPosition = cameraView.Pos,
            CameraRotation = cameraView.ViewRot,
            FringeTileUris = _fringeTiles.Select(t => t.GetUri()).ToList(), // GetUri() needs to be implemented
            TileHierarchy = new Dictionary<string, string>()
        };

        // Traverse cache to build hierarchy map
        foreach (var tile in _tileCache.Values)
        {
            if (tile.Parent != null)
            {
                state.TileHierarchy[tile.GetUri()] = tile.Parent.GetUri();
            }
        }

        string json = System.Text.Json.JsonSerializer.Serialize(state);
        System.IO.File.WriteAllText("session.json", json);
    }
    ```
3.  **Add `GetUri()` to `Tile.cs`:** This method should return a unique identifier for the tile, which would be its `ChildJson` or `GlbFile` URI, or a special token like "root".

### Step 2.3: Implement Load Logic

**File:** `cs/MainWindow.xaml.cs`

1.  **Modify `MainWindow` Constructor:**
    *   The startup logic needs to decide whether to load from `session.json` or start fresh.

    ```csharp
    public MainWindow()
    {
        this.DataContext = this;
        InitializeComponent();
        cameraView = new CameraView();
        earthViz = new EarthViz();
        frustumViz = new FrustumViz();
        veldridRenderer.cameraView = cameraView;

        if (System.IO.File.Exists("session.json"))
        {
            LoadSessionState().ContinueWith(t =>
            {
                veldridRenderer.OnRender = OnRender;
            });
        }
        else
        {
            // Original startup logic
            GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty).ContinueWith(t =>
            {
                // ...
            });
        }
    }
    ```

2.  **Implement `LoadSessionState()`:**
    ```csharp
    private async Task LoadSessionState()
    {
        string json = System.IO.File.ReadAllText("session.json");
        var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json);

        // Restore camera
        cameraView.SetPositionAndRotation(state.CameraPosition, state.CameraRotation); // New method in CameraView

        // Asynchronously load all tiles that were part of the last session
        var allUris = state.TileHierarchy.Keys.Union(state.TileHierarchy.Values).Distinct();
        var loadTasks = allUris.Select(uri => GoogleTile.CreateFromUri(uri, string.Empty).ContinueWith(t => {
            _tileCache[uri] = new Tile(t.Result.root, null);
        })).ToList();
        await Task.WhenAll(loadTasks);

        // Reconstruct the parent-child hierarchy
        foreach (var entry in state.TileHierarchy)
        {
            if (_tileCache.TryGetValue(entry.Key, out Tile child) && _tileCache.TryGetValue(entry.Value, out Tile parent))
            {
                child.Parent = parent;
                // This part is tricky; the original children array needs to be reconstructed
            }
        }
        
        // Restore the fringe
        _fringeTiles = state.FringeTileUris.Select(uri => _tileCache[uri]).ToHashSet();
        _root = _tileCache["root"];

        RefreshTiles();
    }
    ```
This plan provides a high-level blueprint. The actual implementation will require careful handling of asynchronous operations, state management, and edge cases, particularly in reconstructing the `ChildTiles` array from the saved hierarchy.