using Browsingway.Common;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;

namespace Browsingway;

public class Plugin : IDalamudPlugin
{
	private const string _command = "/t3";

	private readonly DependencyManager _dependencyManager;
	private readonly Dictionary<Guid, Overlay> _overlays = new();
	private readonly WindowSystem _windowSystem = new("T3Linkpearl");
	private readonly string _pluginConfigDir;
	private readonly string _pluginDir;
	private readonly ISharedImmediateTexture _bubbleIcon;
	private T3StatusMonitor? _statusMonitor;
	private T3ConnectionMonitor? _connectionMonitor;

	private RenderProcess? _renderProcess;
	private ActHandler _actHandler;
	private Settings? _settings;
	private Services _services;
	private bool _bubbleDragging;
	private bool _bubbleHovered;
	private const float BubbleWindowSize = 64f;
	private const float BubbleIconSize = 52f;

	public Plugin(IDalamudPluginInterface pluginInterface)
	{
		// init services
		_services = pluginInterface.Create<Services>()!;

		_pluginDir = pluginInterface.AssemblyLocation.DirectoryName ?? "";
		if (String.IsNullOrEmpty(_pluginDir))
		{
			throw new Exception("Could not determine plugin directory");
		}

		_pluginConfigDir = pluginInterface.GetPluginConfigDirectory();
		_bubbleIcon = Services.TextureProvider.GetFromFile(Path.Combine(_pluginDir, "t3code.png"));

		_actHandler = new ActHandler();

		_dependencyManager = new DependencyManager(_pluginDir, _pluginConfigDir);
		_dependencyManager.DependenciesReady += (_, _) => DependenciesReady();
		_dependencyManager.Initialise();

		// Hook up render hook
		pluginInterface.UiBuilder.Draw += Render;
	}

	// Required for LivePluginLoader support
	public string AssemblyLocation { get; } = Assembly.GetExecutingAssembly().Location;
	public string Name => "T3 Linkpearl";

	public void Dispose()
	{
		foreach (Overlay overlay in _overlays.Values) { overlay.Dispose(); }

		_overlays.Clear();

		_renderProcess?.Dispose();

		_settings?.Dispose();
		_windowSystem.RemoveAllWindows();
		_statusMonitor?.Dispose();
		_connectionMonitor?.Dispose();

		Services.CommandManager.RemoveHandler(_command);

		WndProcHandler.Shutdown();
		DxHandler.Shutdown();

		_dependencyManager.Dispose();
	}

	private void DependenciesReady()
	{
		// Spin up DX handling from the plugin interface
		DxHandler.Initialise(Services.PluginInterface);

		// Spin up WndProc hook
		WndProcHandler.Initialise(DxHandler.WindowHandle);
		WndProcHandler.WndProcMessage += OnWndProc;

		// Boot the render process. This has to be done before initialising settings to prevent a
		// race condition overlays receiving a null reference.
		int pid = Process.GetCurrentProcess().Id;
		_renderProcess = new RenderProcess(pid, _pluginDir, _pluginConfigDir, _dependencyManager, Services.PluginLog);
		_renderProcess.Rpc!.RendererReady += msg =>
		{
			if (!msg.HasDxSharedTexturesSupport)
			{
				Services.PluginLog.Error("Could not initialize shared textures transport. T3 Linkpearl will not work.");
				return;
			}

			Services.Framework.RunOnFrameworkThread(() =>
			{
				if (_settings is not null)
				{
					_settings.HydrateOverlays();
				}
			});
		};
		_renderProcess.Rpc.SetCursor += msg =>
		{
			Services.Framework.RunOnFrameworkThread(() =>
			{
				Guid guid = new(msg.Guid.Span);
				Overlay? overlay = _overlays.Values.FirstOrDefault(overlay => overlay.RenderGuid == guid);
				overlay?.SetCursor(msg.Cursor);
			});
		};
		_renderProcess.Rpc.UpdateTexture += msg =>
		{
			Services.Framework.RunOnFrameworkThread(() =>
			{
				Guid guid = new(msg.Guid.Span);
				if (_overlays.TryGetValue(guid, out Overlay? overlay))
				{
					overlay.SetTexture((IntPtr)msg.TextureHandle);
				}
				else
				{
					Services.PluginLog.Error("Overlay Id not found");
				}
			});
		};
		_renderProcess.Start();

		// Prep settings
		_settings = new Settings();
		_windowSystem.AddWindow(_settings);
		_statusMonitor = new T3StatusMonitor(
			_pluginConfigDir,
			_settings.Config.T3DataDirectory,
			_settings.LastAcknowledgedT3ActivityAt);
		_settings.T3DatabasePathProvider = () => _statusMonitor.DatabasePath;
		_settings.T3DataDirectoryChanged += (_, path) => _statusMonitor.SetDataDirectory(path);
		_connectionMonitor = new T3ConnectionMonitor(_settings.PrimaryOverlay?.Url ?? "http://127.0.0.1:3773");
		_settings.T3ConnectionStatusProvider = () => _connectionMonitor.Status;
		_settings.RetryT3Connection = _connectionMonitor.Retry;
		if (_settings is not null)
		{
			_settings.OverlayAdded += OnOverlayAdded;
			_settings.OverlayNavigated += OnOverlayNavigated;
			_settings.OverlayDebugged += OnOverlayDebugged;
			_settings.OverlayRemoved += OnOverlayRemoved;
			_settings.OverlayZoomed += OnOverlayZoomed;
			_settings.OverlayMuted += OnOverlayMuted;
			_actHandler.AvailabilityChanged += OnActAvailabilityChanged;
			_settings.OverlayUserCssChanged += OnUserCssChanged;
		}

		// Hook up the main BW command
		Services.CommandManager.AddHandler(_command,
			new CommandInfo(HandleCommand) {HelpMessage = "Open T3 Code in-game. Use '/t3', '/t3 show', or '/t3 config'.", ShowInHelp = true});
	}

	private (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
	{
		// Notify all the overlays of the wndproc, respond with the first capturing response (if any)
		// TODO: Yeah this ain't great but realistically only one will capture at any one time for now.
		IEnumerable<(bool, long)> responses = _overlays.Select(pair => pair.Value.WndProcMessage(msg, wParam, lParam));
		return responses.FirstOrDefault(pair => pair.Item1);
	}

	private void OnActAvailabilityChanged(object? sender, bool e)
	{
		_settings?.OnActAvailabilityChanged(e);
	}

	private void OnOverlayAdded(object? sender, InlayConfiguration overlayConfig)
	{
		if (_renderProcess is null || _settings is null)
		{
			return;
		}

		Overlay overlay = new(_renderProcess, overlayConfig, _settings.Config, _pluginDir);
		_overlays.TryAdd(overlayConfig.Guid, overlay);
	}

	private void OnOverlayNavigated(object? sender, InlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Navigate(config.Url);
		if (_settings?.PrimaryOverlay?.Guid == config.Guid)
			_connectionMonitor?.SetUrl(config.Url);
	}

	private void OnOverlayDebugged(object? sender, InlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Debug();
	}

	private void OnOverlayRemoved(object? sender, InlayConfiguration config)
	{
		if (_overlays.Remove(config.Guid, out var overlay))
		{
			overlay.Dispose();
		}
	}

	private void OnOverlayZoomed(object? sender, InlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Zoom(config.Zoom);
	}

	private void OnOverlayMuted(object? sender, InlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var overlay))
			overlay.Mute(config.Muted);
	}

	private void OnUserCssChanged(object? sender, InlayConfiguration config)
	{
		Overlay overlay = _overlays[config.Guid];
		overlay.InjectUserCss(config.CustomCss);
	}

	private void Render()
	{
		_dependencyManager.Render();
		_windowSystem.Draw();

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

		_renderProcess?.EnsureRenderProcessIsAlive();
		_actHandler.Check();

		InlayConfiguration? primaryOverlay = _settings?.PrimaryOverlay;
		Vector2? bubblePosition = GetBubblePosition();
		foreach (Overlay overlay in _overlays.Values)
		{
			bool attachToBubble = _settings?.Config.ShowBubble == true && primaryOverlay?.Guid == overlay.RenderGuid;
			overlay.SetBubbleAnchor(attachToBubble ? bubblePosition : null, BubbleWindowSize);
			overlay.SetBubbleHovered(attachToBubble && _bubbleHovered);
			overlay.Render();
		}

		ImGui.PopStyleVar();
		RenderBubble();
	}

	private void RenderBubble()
	{
		if (_settings is null || !_settings.Config.ShowBubble) return;
		InlayConfiguration? primaryOverlay = _settings.PrimaryOverlay;
		if (primaryOverlay is null) return;

		Vector2 bubblePosition = GetBubblePosition()!.Value;
		ImGui.SetNextWindowPos(bubblePosition, ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Vector2(BubbleWindowSize, BubbleWindowSize), ImGuiCond.Always);
		ImGui.SetNextWindowBgAlpha(0f);
		ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
		                         | ImGuiWindowFlags.NoScrollbar
		                         | ImGuiWindowFlags.NoScrollWithMouse
		                         | ImGuiWindowFlags.NoDocking
		                         | ImGuiWindowFlags.NoNav;

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

		ImGui.Begin("T3 Code###T3CodeBubble", flags);
		Vector2 iconMin = ImGui.GetWindowPos() + new Vector2(4f, 4f);
		Vector2 iconMax = iconMin + new Vector2(BubbleIconSize, BubbleIconSize);
		ImGui.SetCursorPos(new Vector2(4f, 4f));
		bool clicked = ImGui.InvisibleButton("##T3CodeBubbleButton", new Vector2(BubbleIconSize, BubbleIconSize));
		bool hovered = ImGui.IsItemHovered();
		_bubbleHovered = hovered;
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		drawList.AddImageRounded(
			_bubbleIcon.GetWrapOrEmpty().Handle,
			iconMin,
			iconMax,
			Vector2.Zero,
			Vector2.One,
			0xFFFFFFFF,
			15f);
		if (hovered || _bubbleDragging)
		{
			drawList.AddRect(iconMin - Vector2.One, iconMax + Vector2.One, 0xFFD8C6FF, 16f, ImDrawFlags.None, 2f);
		}
		if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
		{
			_bubbleDragging = true;
		}

		if (_bubbleDragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			bubblePosition = ClampBubblePosition(bubblePosition + ImGui.GetIO().MouseDelta);
			_settings.SetBubblePosition(bubblePosition, false);
			ImGui.SetWindowPos(bubblePosition, ImGuiCond.Always);
		}

		if (clicked && !_bubbleDragging)
		{
			bool opening = primaryOverlay.Hidden;
			if (opening && _connectionMonitor?.Status.IsReachable == false)
			{
				Services.Chat.PrintError($"T3 Code is not reachable at {primaryOverlay.Url}. Start T3 Code or check /t3 config.");
			}
			if (opening && _statusMonitor is not null)
			{
				_statusMonitor.Acknowledge(_settings.AcknowledgeT3Activity());
			}
			_settings.TogglePrimaryOverlay();
			if (opening && _overlays.TryGetValue(primaryOverlay.Guid, out Overlay? overlay))
			{
				overlay.FocusFromBubble();
			}
		}

		if (_bubbleDragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
		{
			_bubbleDragging = false;
			_settings.SetBubblePosition(bubblePosition, true);
		}
		T3ThreadStatus status = _statusMonitor?.Status ?? T3ThreadStatus.Empty;
		RenderStatusBadges(drawList, iconMin, iconMax, status);
		if (hovered)
		{
			ImGui.SetTooltip(BuildBubbleTooltip(primaryOverlay.Hidden, status, _connectionMonitor?.Status));
		}
		ImGui.End();

		ImGui.PopStyleVar();
	}

	private static void RenderStatusBadges(ImDrawListPtr drawList, Vector2 iconMin, Vector2 iconMax, T3ThreadStatus status)
	{
		if (status.Working > 0)
		{
			DrawBadge(drawList, new Vector2(iconMin.X + 9f, iconMax.Y - 5f), status.Working, 0xFFF59E0B);
		}

		if (status.NeedsAttention > 0)
		{
			uint color = status.DirectAttention > 0 || status.Failed > 0 ? 0xFF4F46E5 : 0xFF22C55E;
			DrawBadge(drawList, new Vector2(iconMax.X - 5f, iconMin.Y + 9f), status.NeedsAttention, color);
		}
	}

	private static void DrawBadge(ImDrawListPtr drawList, Vector2 centre, int count, uint color)
	{
		const float radius = 10f;
		string label = count > 99 ? "99+" : count.ToString();
		drawList.AddCircleFilled(centre, radius + 2f, 0xF2181622);
		drawList.AddCircleFilled(centre, radius, color);
		Vector2 textSize = ImGui.CalcTextSize(label);
		drawList.AddText(centre - textSize / 2f, 0xFFFFFFFF, label);
	}

	private static string BuildBubbleTooltip(bool hidden, T3ThreadStatus status, T3ConnectionStatus? connection)
	{
		List<string> details = new();
		if (connection?.IsReachable == false) details.Add("T3 Code is not reachable");
		if (status.Working > 0) details.Add($"{status.Working} working");
		if (status.DirectAttention > 0) details.Add($"{status.DirectAttention} awaiting you");
		if (status.Completed > 0) details.Add($"{status.Completed} completed");
		if (status.Failed > 0) details.Add($"{status.Failed} failed");
		string action = hidden ? "Open T3 Code" : "Hide T3 Code";
		return details.Count == 0 ? action : $"{action}\n{string.Join("  •  ", details)}";
	}

	private Vector2? GetBubblePosition()
	{
		if (_settings is null || !_settings.Config.ShowBubble) return null;

		Vector2 position = new(_settings.Config.BubbleX, _settings.Config.BubbleY);
		if (position.X < 0f || position.Y < 0f)
		{
			ImGuiViewportPtr viewport = ImGui.GetMainViewport();
			position = new Vector2(
				viewport.WorkPos.X + viewport.WorkSize.X - BubbleWindowSize - 24f,
				viewport.WorkPos.Y + viewport.WorkSize.Y / 2f - BubbleWindowSize / 2f);
			_settings.SetBubblePosition(position, true);
		}

		return ClampBubblePosition(position);
	}

	private static Vector2 ClampBubblePosition(Vector2 position)
	{
		ImGuiViewportPtr viewport = ImGui.GetMainViewport();
		Vector2 min = viewport.WorkPos;
		Vector2 max = viewport.WorkPos + viewport.WorkSize - new Vector2(BubbleWindowSize, BubbleWindowSize);
		return Vector2.Clamp(position, min, max);
	}

	private void HandleCommand(string command, string rawArgs)
	{
		// Docs complain about perf of multiple splits.
		// I'm not convinced this is a sufficiently perf-critical path to care.
		string[] args = rawArgs.Split(null as char[], 2, StringSplitOptions.RemoveEmptyEntries);

		if (args.Length == 0)
		{
			_settings?.TogglePrimaryOverlay();
			return;
		}

		string subcommandArgs = args.Length > 1 ? args[1] : "";

		switch (args[0])
		{
			case "show":
				_settings?.ShowPrimaryOverlay();
				break;
			case "toggle":
				_settings?.TogglePrimaryOverlay();
				break;
			case "config":
				_settings?.HandleConfigCommand(subcommandArgs);
				break;
			case "inlay":
				_settings?.HandleOverlayCommand(subcommandArgs);
				break;
			case "overlay":
				_settings?.HandleOverlayCommand(subcommandArgs);
				break;
			default:
				Services.Chat.PrintError(
					$"Unknown subcommand '{args[0]}'. Valid subcommands are: show,toggle,config,overlay.");
				break;
		}
	}
}
