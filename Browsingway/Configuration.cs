using Browsingway.Common.Ipc;
using Dalamud.Configuration;

namespace Browsingway;

[Serializable]
internal class Configuration : IPluginConfiguration
{
	public List<InlayConfiguration> Inlays = new();
	public float BubbleX = -1f;
	public float BubbleY = -1f;
	public string LastAcknowledgedT3ActivityAt = "";
	public bool ShowBubble = true;
	public bool AnimateVisibility = true;
	public bool DimWhenInactive = true;
	public float IdleOpacity = 45f;
	public string T3DataDirectory = "";
	public int Version { get; set; } = 0;
}

[Serializable]
internal class InlayConfiguration
{
	public bool ClickThrough;
	public int Framerate = 60;
	public Guid Guid;
	public bool Hidden;
	public bool Locked;
	public string Name = null!;
	public float Opacity = 100f;
	public bool TypeThrough;
	public string Url = null!;
	public float Zoom = 100f;
	public bool Disabled;
	public string CustomCss = "";
	public bool Muted;
	public bool ActOptimizations;
	public bool Fullscreen;
	public bool HideOutOfCombat = false;
	public bool HideInPvP = false;
	public int HideDelay = 0;
}
