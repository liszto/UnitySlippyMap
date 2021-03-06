// 
//  TileLayer.cs
//  
//  Author:
//       Jonathan Derrough <jonathan.derrough@gmail.com>
//  
//  Copyright (c) 2012 Jonathan Derrough
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using UnityEngine;

using System;
using System.Collections.Generic;

using UnitySlippyMap;

// <summary>
// An abstract class representing a tile layer.
// One can derive from it to leverage specific or custom tile services.
// </summary>
public abstract class TileLayer : Layer
{
	public string								URLFormat;
	public int									TileCacheSizeLimit = 100;
	public int									TileSize = 256;
	
	// shared tile template
	protected static GameObject					tileTemplate;
	// tile template "ref counter"
	protected static int						tileTemplateUseCount = 0;
	
	protected Dictionary<string, GameObject>	tiles = new Dictionary<string, GameObject>();
	protected List<GameObject>					tileCache = new List<GameObject>();
	protected List<string>						visitedTiles = new List<string>();
	
	protected enum NeighbourTileDirection 
	{
		North,
		South,
		East,
		West
	}
	
	#region MonoBehaviour implementation
	
	void Awake()
	{
		// create the tile template if needed
		if (tileTemplate == null)
		{
			tileTemplate = Tile.CreateTileTemplate();
			tileTemplate.hideFlags = HideFlags.HideAndDontSave;
			tileTemplate.renderer.enabled = false;
		}
		++tileTemplateUseCount;
	}
	
	// Use this for initialization
	void Start ()
	{		
		if (tileTemplate.transform.localScale.x != Map.RoundedHalfMapScale)
			tileTemplate.transform.localScale = new Vector3(Map.RoundedHalfMapScale, 1.0f, Map.RoundedHalfMapScale);
	}
	
	void OnDestroy()
	{
		--tileTemplateUseCount;
		
		// destroy the tile template if nobody is using anymore
		if (tileTemplate != null && tileTemplateUseCount == 0)
			DestroyImmediate(tileTemplate);
	}
	
	void Update()
	{
	}
	
	#endregion
	
	#region Layer implementation
	
	public override void UpdateContent()
	{
		if (tileTemplate.transform.localScale.x != Map.RoundedHalfMapScale)
			tileTemplate.transform.localScale = new Vector3(Map.RoundedHalfMapScale, 1.0f, Map.RoundedHalfMapScale);
		
        if (Camera.main != null)
        {
    		Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(Camera.main);
    		
    		CleanUpTiles(frustum, Map.RoundedZoom);
    		
    		visitedTiles.Clear();
    
    		UpdateTiles(frustum);
        }
		
		// move the tiles by the map's root translation
		Vector3 displacement = Map.gameObject.transform.position;
		if (displacement != Vector3.zero)
		{
			foreach (KeyValuePair<string, GameObject> tile in tiles)
			{
				tile.Value.transform.position += displacement;
			}
		}
	}
	
	#endregion
	
	#region Protected methods
	
	protected static string	tileAddressLookedFor;
	protected static bool visitedTilesMatchPredicate(string tileAddress)
	{
		if (tileAddress == tileAddressLookedFor)
			return true;
		return false;
	}
	
	#endregion
		
	#region Private methods
	
	// <summary>
	// Removes the tiles outside of the camera frustum and zoom level.
	// </summary>
	private void CleanUpTiles(Plane[] frustum, int roundedZoom)
	{
		List<string> tilesToRemove = new List<string>();
		foreach (KeyValuePair<string, GameObject> pair in tiles)
		{
			if (GeometryUtility.TestPlanesAABB(frustum, pair.Value.collider.bounds) == false
				|| pair.Key.StartsWith(roundedZoom + "_") == false)
			{
				string[] tileAddressTokens = pair.Key.Split(new char[] { '_' });
				TileDownloader.Instance.Cancel(String.Format(URLFormat, tileAddressTokens[0], tileAddressTokens[1], tileAddressTokens[2]));
				tilesToRemove.Add(pair.Key);
				
				Renderer renderer = pair.Value.renderer;
				if (renderer != null)
				{
					GameObject.DestroyImmediate(renderer.material.mainTexture);
					renderer.material.mainTexture = null;
				}
				pair.Value.active = false;
				
#if DEBUG_LOG
				Debug.Log("DEBUG: remove tile: " + pair.Key);
#endif
			}
		}
		foreach (string tileAddress in tilesToRemove)
		{
			GameObject tile = tiles[tileAddress];
			tiles.Remove(tileAddress);
			tileCache.Add(tile);
		}
	}

	// <summary>
	// Updates the tiles in respect to the camera frustum and the map's zoom level.
	// </summary>
	private void UpdateTiles(Plane[] frustum)
	{
		int tileX, tileY;
		int tileCountOnX, tileCountOnY;
		float offsetX, offsetZ;
		
		GetTileCountPerAxis(out tileCountOnX, out tileCountOnY);
		GetCenterTile(out tileX, out tileY, out offsetX, out offsetZ);
		GrowTiles(frustum, tileX, tileY, tileCountOnX, tileCountOnY, offsetX, offsetZ);
	}
	
	// <summary>
	// A recursive method that grows tiles starting from the map's center in all four directions.
	// </summary>
	void GrowTiles(Plane[] frustum, int tileX, int tileY, int tileCountOnX, int tileCountOnY, float offsetX, float offsetZ)
	{
		tileTemplate.transform.position = new Vector3(offsetX, tileTemplate.transform.position.y, offsetZ);
		if (GeometryUtility.TestPlanesAABB(frustum, tileTemplate.collider.bounds) == true)
		{
			string tileAddress = Map.RoundedZoom + "_" + tileX + "_" + tileY;
			//Debug.Log("DEBUG: tile address: " + tileAddress);
			if (tiles.ContainsKey(tileAddress) == false)
			{
				GameObject tile = null;
				if (tileCache.Count > 0)
				{
					tile = tileCache[0];
					tileCache.Remove(tile);
					tile.transform.position = tileTemplate.transform.position;
					tile.transform.localScale = new Vector3(Map.RoundedHalfMapScale, 1.0f, Map.RoundedHalfMapScale);
					tile.active = true;
				}
				else
				{
					tile = GameObject.Instantiate(tileTemplate) as GameObject;
					tile.transform.parent = this.gameObject.transform;
				}
				
				tile.name = "tile_" + tileAddress;
				tiles.Add(tileAddress, tile);
				MeshRenderer tileMeshRenderer = tile.GetComponent<MeshRenderer>();
				tileMeshRenderer.enabled = true;
				
				TileDownloader.Instance.Get(String.Format(URLFormat, Map.RoundedZoom, tileX, tileY), tileMeshRenderer.material);
			}
			
			tileAddressLookedFor = tileAddress;
			if (visitedTiles.Exists(visitedTilesMatchPredicate) == false)
			{
				visitedTiles.Add(tileAddress);

				// grow tiles in the four directions without getting outside of the coordinate range of the zoom level
				int nTileX, nTileY;
				float nOffsetX, nOffsetZ;

				if (GetNeighbourTile(tileX, tileY, offsetX, offsetZ, tileCountOnX, tileCountOnY, NeighbourTileDirection.South, out nTileX, out nTileY, out nOffsetX, out nOffsetZ))
					GrowTiles(frustum, nTileX, nTileY, tileCountOnX, tileCountOnY, nOffsetX, nOffsetZ);

				if (GetNeighbourTile(tileX, tileY, offsetX, offsetZ, tileCountOnX, tileCountOnY, NeighbourTileDirection.North, out nTileX, out nTileY, out nOffsetX, out nOffsetZ))
					GrowTiles(frustum, nTileX, nTileY, tileCountOnX, tileCountOnY, nOffsetX, nOffsetZ);

				if (GetNeighbourTile(tileX, tileY, offsetX, offsetZ, tileCountOnX, tileCountOnY, NeighbourTileDirection.East, out nTileX, out nTileY, out nOffsetX, out nOffsetZ))
					GrowTiles(frustum, nTileX, nTileY, tileCountOnX, tileCountOnY, nOffsetX, nOffsetZ);

				if (GetNeighbourTile(tileX, tileY, offsetX, offsetZ, tileCountOnX, tileCountOnY, NeighbourTileDirection.West, out nTileX, out nTileY, out nOffsetX, out nOffsetZ))
					GrowTiles(frustum, nTileX, nTileY, tileCountOnX, tileCountOnY, nOffsetX, nOffsetZ);
			}
		}
	}
	
	#endregion
	
	#region TileLayer interface
	
	// <summary>
	// Writes the numbers of tiles on each axis in respect to the map's zoom level.
	// </summary>
	protected abstract void GetTileCountPerAxis(out int tileCountOnX, out int tileCountOnY);

	// <summary>
	// Writes the tile coordinates and offsets to the origin for the tile under the center of the map.
	// </summary>
	protected abstract void GetCenterTile(out int tileX, out int tileY, out float offsetX, out float offsetZ);

	// <summary>
	// Writes the tile coordinates and offsets to the origin for the neighbour tile in the specified direction.
	// </summary>
	protected abstract bool GetNeighbourTile(int tileX, int tileY, float offsetX, float offsetY, int tileCountOnX, int tileCountOnY, NeighbourTileDirection dir, out int nTileX, out int nTileY, out float nOffsetX, out float nOffsetZ);
	
	#endregion
	
}

