
# Revised Implementation Plan: 3D Tiles Optimization

This document outlines the revised steps required to implement the "Neighbor-Based Traversal" and "Session Persistence" features as described in `Google3DTilesRendering.md`, addressing the issues identified in the original implementation plan.

---

## Implementation Strategy

The implementation will be done in **4 phases** to minimize risk and ensure each component works correctly before adding complexity:

### Testing
After each phase, test that the project builds using the command: dotnet build cs/googletiles.csproj

1. **Phase 1**: Foundation - Add missing properties and methods
2. **Phase 2**: Core fringe-based traversal (simplified)
3. **Phase 3**: Caching system with proper eviction
4. **Phase 4**: Session persistence

---

## Phase 1: Foundation - Missing Properties and Methods

### Step 1.1: Extend Tile Class

**File:** `cs/Tile.cs`

1. **Add Missing Properties:**
   ```csharp
   public bool IsDownloading { get; set; } = false;
   public int LastVisibleFrame { get; set; } = 0;
   ```

2. **Add GetGeometricError Method:**
   ```csharp
   public float GetGeometricError(CameraView cv)
   {
       if (Bounds.IsInside(cv)) 
           return float.PositiveInfinity; // Always refine if camera is inside
       
       float distSq = Bounds.DistanceSqFromPt(cv.Pos);
       if (distSq <= 0) 
           return float.PositiveInfinity;
       
       float dist = MathF.Sqrt(distSq);
       if (GeometricError == 0) 
           return float.PositiveInfinity; // Leaf node that can't be refined
       
       return dist / GeometricError;
   }
   ```

3. **Add GetUri Method:**
   ```csharp
   public string GetUri()
   {
       if (Parent == null) return "root";
       if (!string.IsNullOrEmpty(ChildJson)) return ChildJson;
       if (!string.IsNullOrEmpty(GlbFile)) return GlbFile;
       return $"tile_{tileIdx}";
   }
   ```

4. **Modify DownloadChildren to Set IsDownloading Flag:**
   ```csharp
   public async Task<bool> DownloadChildren(string sessionkey, CameraView cv, int frameIdx, bool saveGlb)
   {
       LastVisitedFrame = frameIdx;
       IsInView = bounds.IsInView(cv);
       if (!IsInView)
           return false;

       List<Task<bool>> allTasks = new List<Task<bool>>();
       if (!this.childrenDownloaded)
       {
           if (GlbFile != null && mesh == null)
           {
               allTasks.Add(DownloadGlb(sessionkey));
           }

           this.childrenDownloaded = true;
           List<Tile> tiles = new List<Tile>();
           if (ChildJson != null)
           {
               if (ChildTiles?.Length > 0)
                   Debugger.Break();
               
               IsDownloading = true; // Set flag before download
               GoogleTile tile = await GoogleTile.CreateFromUri(ChildJson, sessionkey);
               IsDownloading = false; // Clear flag after download
               
               JSONCnt++;
               Tile t = new Tile(tile.root, this);
               tiles.Add(t);
               ChildTiles = tiles.ToArray();
           }
       }

       // ... rest of method remains the same
   }
   ```

### Step 1.2: Extend CameraView Class

**File:** `cs/CameraView.cs`

1. **Add SetPositionAndRotation Method:**
   ```csharp
   public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
   {
       camPos = position;
       camRot = rotation;
       Update(); // Ensure matrices are recalculated
   }
   ```

---

## Phase 2: Core Fringe-Based Traversal

### Step 2.1: Add Configuration Constants

**File:** `cs/MainWindow.xaml.cs`

1. **Add Configuration Constants:**
   ```csharp
   // Geometric error thresholds
   private const float REFINE_THRESHOLD = 40.0f;   // Matches current hardcoded value
   private const float COARSEN_THRESHOLD = 80.0f;  // 2x refine for hysteresis
   private const int CACHE_RETENTION_FRAMES = 300; // ~10 seconds at 30fps
   ```

### Step 2.2: Add New State Variables

**File:** `cs/MainWindow.xaml.cs`

1. **Replace root with new state management:**
   ```csharp
   // Replace: Tile root;
   private Tile _root;
   private HashSet<Tile> _fringeTiles = new HashSet<Tile>();
   private Dictionary<string, Tile> _tileCache = new Dictionary<string, Tile>();
   ```

### Step 2.3: Modify Constructor

**File:** `cs/MainWindow.xaml.cs`

1. **Update initialization logic:**
   ```csharp
   public MainWindow()
   {
       this.DataContext = this;
       InitializeComponent();
       cameraView = new CameraView();
       earthViz = new EarthViz();
       frustumViz = new FrustumViz();
       veldridRenderer.cameraView = cameraView;
       
       GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty).ContinueWith(t =>
       {
           GoogleTile rootTile = t.Result;
           sessionkey = rootTile.GetSession();
           _root = new Tile(rootTile.root, null);
           
           // Initialize fringe and cache
           _tileCache.Add("root", _root);
           _fringeTiles.Add(_root);
           
           RefreshTiles();
           veldridRenderer.OnRender = OnRender;
       });
   }
   ```

### Step 2.4: Implement Simplified Fringe Update

**File:** `cs/MainWindow.xaml.cs`

1. **Replace OnRender method:**
   ```csharp
   void OnRender(CommandList _cl, GraphicsDevice _gd, Swapchain _sc)
   {
       cameraView?.Update();
       frameIdx++;

       if (DownloadEnabled)
       {
           UpdateTilesBasedOnFringe();
       }

       // ... rest of rendering logic remains the same
       
       if (earthViz != null)
           earthViz.Draw(_cl, cameraView, _root, frameIdx);
       
       // ... property change notifications remain the same
   }
   ```

2. **Implement Simplified UpdateTilesBasedOnFringe:**
   ```csharp
   private async void UpdateTilesBasedOnFringe()
   {
       HashSet<Tile> nextFringe = new HashSet<Tile>();
       List<Task> downloadTasks = new List<Task>();
       
       foreach (var tile in _fringeTiles.ToList()) // ToList to avoid modification during iteration
       {
           // Skip tiles that are no longer in view
           if (!tile.Bounds.IsInView(cameraView))
           {
               // Move up to parent if available
               if (tile.Parent != null)
               {
                   nextFringe.Add(tile.Parent);
               }
               continue;
           }
           
           tile.LastVisibleFrame = frameIdx;
           float error = tile.GetGeometricError(cameraView);
           
           // Decision logic
           if (error > REFINE_THRESHOLD)
           {
               // Need more detail - try to refine
               if (tile.ChildTiles != null && tile.ChildTiles.Length > 0)
               {
                   // Add children to fringe
                   foreach (var child in tile.ChildTiles)
                   {
                       nextFringe.Add(child);
                   }
               }
               else if (!string.IsNullOrEmpty(tile.ChildJson) && !tile.IsDownloading)
               {
                   // Need to download children
                   downloadTasks.Add(tile.DownloadChildren(sessionkey, cameraView, frameIdx, false));
                   nextFringe.Add(tile); // Keep in fringe until children are available
               }
               else
               {
                   // Leaf node or already downloading
                   nextFringe.Add(tile);
               }
           }
           else if (error < COARSEN_THRESHOLD && tile.Parent != null)
           {
               // Too much detail - coarsen to parent
               nextFringe.Add(tile.Parent);
           }
           else
           {
               // Appropriate level of detail
               nextFringe.Add(tile);
           }
       }
       
       _fringeTiles = nextFringe;
       
       // Wait for any downloads to complete
       if (downloadTasks.Any())
       {
           await Task.WhenAll(downloadTasks);
       }
   }
   ```

---

## Phase 3: Caching System with Proper Eviction

### Step 3.1: Implement Cache Management

**File:** `cs/MainWindow.xaml.cs`

1. **Add cache eviction method:**
   ```csharp
   private void EvictOldTiles()
   {
       var tilesToEvict = _tileCache.Values
           .Where(t => frameIdx - t.LastVisibleFrame > CACHE_RETENTION_FRAMES)
           .ToList();
       
       foreach (var tile in tilesToEvict)
       {
           string uri = tile.GetUri();
           _tileCache.Remove(uri);
           
           // Free GPU resources
           if (tile.mesh != null)
           {
               tile.mesh._vertexBuffer?.Dispose();
               tile.mesh._indexBuffer?.Dispose();
               tile.mesh._worldBuffer?.Dispose();
               tile.mesh._surfaceTexture?.Dispose();
               tile.mesh._surfaceTextureView?.Dispose();
               tile.mesh._worldTextureSet?.Dispose();
           }
       }
   }
   ```

2. **Integrate cache eviction into OnRender:**
   ```csharp
   void OnRender(CommandList _cl, GraphicsDevice _gd, Swapchain _sc)
   {
       cameraView?.Update();
       frameIdx++;

       if (DownloadEnabled)
       {
           UpdateTilesBasedOnFringe();
           
           // Evict old tiles every 60 frames (~2 seconds at 30fps)
           if (frameIdx % 60 == 0)
           {
               EvictOldTiles();
           }
       }

       // ... rest remains the same
   }
   ```

### Step 3.2: Enhance Tile Management

**File:** `cs/MainWindow.xaml.cs`

1. **Add tile to cache when created:**
   ```csharp
   private void AddTileToCache(Tile tile)
   {
       string uri = tile.GetUri();
       if (!_tileCache.ContainsKey(uri))
       {
           _tileCache[uri] = tile;
       }
   }
   ```

2. **Modify tile creation to use cache:**
   Update the `DownloadChildren` method in `Tile.cs` to work with the cache system by passing a reference to the cache.

---

## Phase 4: Session Persistence

### Step 4.1: Create SessionState Class

**New File:** `cs/SessionState.cs`

```csharp
using System.Collections.Generic;
using System.Numerics;

namespace googletiles
{
    public class SessionState
    {
        public Vector3 CameraPosition { get; set; }
        public Quaternion CameraRotation { get; set; }
        public List<string> FringeTileUris { get; set; } = new List<string>();
        public Dictionary<string, string> TileHierarchy { get; set; } = new Dictionary<string, string>();
        public string SessionKey { get; set; }
    }
}
```

### Step 4.2: Implement Save Logic

**File:** `cs/MainWindow.xaml.cs`

1. **Add window closing handler:**
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

2. **Implement SaveSessionState:**
   ```csharp
   private void SaveSessionState()
   {
       try
       {
           var state = new SessionState
           {
               CameraPosition = cameraView.Pos,
               CameraRotation = cameraView.ViewRot,
               SessionKey = sessionkey,
               FringeTileUris = _fringeTiles.Select(t => t.GetUri()).ToList(),
               TileHierarchy = new Dictionary<string, string>()
           };

           // Build hierarchy map from cache
           foreach (var tile in _tileCache.Values)
           {
               if (tile.Parent != null)
               {
                   state.TileHierarchy[tile.GetUri()] = tile.Parent.GetUri();
               }
           }

           string json = System.Text.Json.JsonSerializer.Serialize(state, new JsonSerializerOptions 
           { 
               WriteIndented = true 
           });
           System.IO.File.WriteAllText("session.json", json);
       }
       catch (Exception ex)
       {
           // Log error but don't prevent application shutdown
           System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
       }
   }
   ```

### Step 4.3: Implement Load Logic

**File:** `cs/MainWindow.xaml.cs`

1. **Modify constructor to check for saved session:**
   ```csharp
   public MainWindow()
   {
       this.DataContext = this;
       InitializeComponent();
       cameraView = new CameraView();
       earthViz = new EarthViz();
       frustumViz = new FrustumViz();
       veldridRenderer.cameraView = cameraView;
       this.Closing += OnMainWindowClosing;

       if (System.IO.File.Exists("session.json"))
       {
           LoadSessionState().ContinueWith(t =>
           {
               if (t.IsCompletedSuccessfully)
               {
                   veldridRenderer.OnRender = OnRender;
               }
               else
               {
                   // Fallback to normal startup
                   StartFreshSession();
               }
           });
       }
       else
       {
           StartFreshSession();
       }
   }

   private void StartFreshSession()
   {
       GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty).ContinueWith(t =>
       {
           GoogleTile rootTile = t.Result;
           sessionkey = rootTile.GetSession();
           _root = new Tile(rootTile.root, null);
           
           _tileCache.Add("root", _root);
           _fringeTiles.Add(_root);
           
           RefreshTiles();
           veldridRenderer.OnRender = OnRender;
       });
   }
   ```

2. **Implement LoadSessionState:**
   ```csharp
   private async Task LoadSessionState()
   {
       try
       {
           string json = System.IO.File.ReadAllText("session.json");
           var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json);

           // Restore camera position
           cameraView.SetPositionAndRotation(state.CameraPosition, state.CameraRotation);
           sessionkey = state.SessionKey;

           // Load all tiles that were part of the last session
           var allUris = state.TileHierarchy.Keys
               .Union(state.TileHierarchy.Values)
               .Union(state.FringeTileUris)
               .
Distinct()
               .ToList();

           // Load root first
           if (!allUris.Contains("root"))
           {
               allUris.Insert(0, "root");
           }

           // Load tiles asynchronously
           var loadTasks = new List<Task<Tile>>();
           foreach (var uri in allUris)
           {
               if (uri == "root")
               {
                   loadTasks.Add(LoadRootTile());
               }
               else
               {
                   loadTasks.Add(LoadTileFromUri(uri, state.SessionKey));
               }
           }

           var loadedTiles = await Task.WhenAll(loadTasks);

           // Build cache
           foreach (var tile in loadedTiles.Where(t => t != null))
           {
               _tileCache[tile.GetUri()] = tile;
           }

           // Set root
           _root = _tileCache["root"];

           // Reconstruct parent-child relationships
           foreach (var entry in state.TileHierarchy)
           {
               if (_tileCache.TryGetValue(entry.Key, out Tile child) && 
                   _tileCache.TryGetValue(entry.Value, out Tile parent))
               {
                   child.Parent = parent;
                   // Note: ChildTiles array reconstruction is complex and may be skipped
                   // in initial implementation. The system can rebuild it through normal traversal.
               }
           }

           // Restore fringe
           _fringeTiles = state.FringeTileUris
               .Where(uri => _tileCache.ContainsKey(uri))
               .Select(uri => _tileCache[uri])
               .ToHashSet();

           // Ensure we have at least the root in the fringe
           if (!_fringeTiles.Any())
           {
               _fringeTiles.Add(_root);
           }

           RefreshTiles();
       }
       catch (Exception ex)
       {
           System.Diagnostics.Debug.WriteLine($"Failed to load session: {ex.Message}");
           throw; // Re-throw to trigger fallback
       }
   }

   private async Task<Tile> LoadRootTile()
   {
       var rootTile = await GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty);
       return new Tile(rootTile.root, null);
   }

   private async Task<Tile> LoadTileFromUri(string uri, string sessionKey)
   {
       try
       {
           var googleTile = await GoogleTile.CreateFromUri(uri, sessionKey);
           return new Tile(googleTile.root, null); // Parent will be set later
       }
       catch
       {
           return null; // Skip tiles that can't be loaded
       }
   }
   ```

---

## Testing and Validation

### Phase 1 Testing
- Verify all new properties and methods compile correctly
- Test geometric error calculation with known tile distances
- Ensure URI generation works for different tile types

### Phase 2 Testing  
- Compare performance: measure tiles processed per frame before/after
- Verify fringe tiles are correctly identified and updated
- Test camera movement scenarios (zoom in/out, panning)

### Phase 3 Testing
- Monitor memory usage over extended sessions
- Verify cache eviction prevents memory leaks
- Test cache hit/miss ratios

### Phase 4 Testing
- Test session save/restore with various camera positions
- Verify graceful fallback when session loading fails
- Test with corrupted session files

---

## Performance Monitoring

Add these metrics to track optimization effectiveness:

```csharp
// Add to MainWindow.xaml.cs
public class PerformanceMetrics
{
    public int TilesProcessedPerFrame { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public int TilesInCache { get; set; }
    public int FringeTileCount { get; set; }
    public TimeSpan FrameProcessingTime { get; set; }
}

private PerformanceMetrics _metrics = new PerformanceMetrics();
```

---

## Risk Mitigation

### Fallback Strategy
Keep the original traversal method available as a fallback:

```csharp
private bool _useFringeTraversal = true;

void OnRender(CommandList _cl, GraphicsDevice _gd, Swapchain _sc)
{
    cameraView?.Update();
    frameIdx++;

    if (DownloadEnabled)
    {
        if (_useFringeTraversal)
        {
            try
            {
                UpdateTilesBasedOnFringe();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fringe traversal failed: {ex.Message}");
                _useFringeTraversal = false; // Fall back to original method
            }
        }
        
        if (!_useFringeTraversal)
        {
            // Original traversal method
            _root.DownloadChildren(sessionkey, cameraView, frameIdx, false);
        }
    }
    
    // ... rest of rendering
}
```

### Configuration Options
Make key parameters configurable:

```csharp
public class TileConfig
{
    public float RefineThreshold { get; set; } = 40.0f;
    public float CoarsenThreshold { get; set; } = 80.0f;
    public int CacheRetentionFrames { get; set; } = 300;
    public bool EnableFringeTraversal { get; set; } = true;
    public bool EnableSessionPersistence { get; set; } = true;
}
```

---

## Summary of Key Improvements

1. **Simplified Fringe Logic**: Removed complex queue-based processing in favor of straightforward parent-child transitions
2. **Proper State Management**: Added `IsDownloading` flag and proper cache management
3. **Calibrated Thresholds**: Used existing system values as baseline with hysteresis
4. **Robust Error Handling**: Added try-catch blocks and fallback strategies
5. **Memory Management**: Implemented proper cache eviction with GPU resource cleanup
6. **Phased Implementation**: Reduced risk by implementing in manageable phases
7. **Performance Monitoring**: Added metrics to validate optimization effectiveness

This revised plan addresses the critical issues identified in the original implementation while maintaining the core benefits of the fringe-based traversal approach.