using UnityEngine;
 
[ExecuteInEditMode]
public class CustomImageEffect : MonoBehaviour {
 
    public Material material;

	void Init()
	{
		Camera.main.depthTextureMode = DepthTextureMode.Depth;
	}
 
    void OnRenderImage(RenderTexture src, RenderTexture dest) {
        Graphics.Blit(src, dest, material);
    }
}