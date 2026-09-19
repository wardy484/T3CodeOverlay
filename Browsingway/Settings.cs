using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Settings : Window, IDisposable
{
	public event EventHandler<InlayConfiguration>? OverlayAdded;
	public event EventHandler<InlayConfiguration>? OverlayNavigated;
	public event EventHandler<InlayConfiguration>? OverlayDebugged;
	public event EventHandler<InlayConfiguration>? OverlayRemoved;
	public event EventHandler<InlayConfiguration>? OverlayZoomed;
	public event EventHandler<InlayConfiguration>? OverlayMuted;
	public event EventHandler<InlayConfiguration>? OverlayUserCssChanged;
	public event EventHandler<string>? T3DataDirectoryChanged;
	public readonly Configuration Config;
	public Func<string?>? T3DatabasePathProvider { get; set; }
	public Func<T3ConnectionStatus>? T3ConnectionStatusProvider { get; set; }
	public Action? RetryT3Connection { get; set; }
	private bool _actAvailable = false;

	private InlayConfiguration? _selectedOverlay;
	private Timer? _saveDebounceTimer;

	public Settings()
		: base(
			"T3 Linkpearl Settings###T3LinkpearlSettings",
			ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
	{
		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(400, 300),
			MaximumSize = new Vector2(9001, 9001),
		};
#if DEBUG
		IsOpen = true;
#endif
		Services.PluginInterface.UiBuilder.OpenConfigUi += Open;
		Config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		if (!DateTime.TryParse(Config.LastAcknowledgedT3ActivityAt, out _))
		{
			Config.LastAcknowledgedT3ActivityAt = DateTime.UtcNow.ToString("O");
			SaveSettings();
		}
		if (Config.Inlays.Count == 0)
		{
			Config.Inlays.Add(new InlayConfiguration
			{
				Guid = Guid.NewGuid(),
				Name = "T3 Code",
				Url = "http://127.0.0.1:3773",
				Hidden = true,
				Framerate = 30,
				Muted = true,
			});
			SaveSettings();
		}
	}

	public InlayConfiguration? PrimaryOverlay => Config.Inlays.FirstOrDefault();

	public void TogglePrimaryOverlay()
	{
		InlayConfiguration? overlay = PrimaryOverlay;
		if (overlay is null) return;
		overlay.Hidden = !overlay.Hidden;
		SaveSettings();
	}

	public void ShowPrimaryOverlay()
	{
		InlayConfiguration? overlay = PrimaryOverlay;
		if (overlay is null || !overlay.Hidden) return;
		overlay.Hidden = false;
		SaveSettings();
	}

	public void SetBubblePosition(Vector2 position, bool save)
	{
		Config.BubbleX = position.X;
		Config.BubbleY = position.Y;
		if (save) SaveSettings();
	}

	public DateTime LastAcknowledgedT3ActivityAt => DateTime.Parse(Config.LastAcknowledgedT3ActivityAt).ToUniversalTime();

	public DateTime AcknowledgeT3Activity()
	{
		DateTime acknowledgedAt = DateTime.UtcNow;
		Config.LastAcknowledgedT3ActivityAt = acknowledgedAt.ToString("O");
		SaveSettings();
		return acknowledgedAt;
	}

	public void Dispose()
	{
		Services.PluginInterface.UiBuilder.OpenConfigUi -= Open;
		_saveDebounceTimer?.Dispose();
	}

	private void Open() => IsOpen = true;

	public void OnActAvailabilityChanged(bool available)
	{
		_actAvailable = available;
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (overlayConfig is { ActOptimizations: true, Disabled: false })
			{
				if (_actAvailable)
					OverlayAdded?.Invoke(this, overlayConfig);
				else
					OverlayRemoved?.Invoke(this, overlayConfig);
			}
		}
	}

	public void HandleConfigCommand(string rawArgs)
	{
		IsOpen = true;

		// TODO: Add further config handling if required here.
	}

	public void HandleOverlayCommand(string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		// Ensure there's enough arguments
		if (args.Length < 2 || (args[1] != "reload" && args.Length < 3))
		{
			Services.Chat.PrintError("Invalid overlay command. Supported syntax: '[overlayCommandName] [setting] [value]'");
			return;
		}

		// Find the matching overlay config
		InlayConfiguration? targetConfig = Config.Inlays.Find(overlay => GetOverlayCommandName(overlay) == args[0]);
		if (targetConfig == null)
		{
			Services.Chat.PrintError(
				$"Unknown overlay '{args[0]}'.");
			return;
		}

		switch (args[1])
		{
			case "url":
				CommandSettingString(args[2], ref targetConfig.Url);
				// TODO: This call is duped with imgui handling. DRY.
				NavigateOverlay(targetConfig);
				break;
			case "locked":
				CommandSettingBoolean(args[2], ref targetConfig.Locked);
				break;
			case "hidden":
				CommandSettingBoolean(args[2], ref targetConfig.Hidden);
				break;
			case "typethrough":
				CommandSettingBoolean(args[2], ref targetConfig.TypeThrough);
				break;
			case "fullscreen":
				CommandSettingBoolean(args[2], ref targetConfig.Fullscreen);
				break;
			case "clickthrough":
				CommandSettingBoolean(args[2], ref targetConfig.ClickThrough);
				break;
			case "muted":
				CommandSettingBoolean(args[2], ref targetConfig.Muted);
				break;
			case "disabled":
				CommandSettingBoolean(args[2], ref targetConfig.Disabled);
				break;
			case "act":
				CommandSettingBoolean(args[2], ref targetConfig.ActOptimizations);
				break;
			case "reload":
				ReloadOverlay(targetConfig);
				break;

			default:
				Services.Chat.PrintError(
					$"Unknown setting '{args[1]}. Valid settings are: url,hidden,locked,fullscreen,clickthrough,typethrough,muted,disabled,act.");
				return;
		}

		SaveSettings();
	}

	private void CommandSettingString(string value, ref string target)
	{
		target = value;
	}

	private void CommandSettingBoolean(string value, ref bool target)
	{
		switch (value)
		{
			case "on":
				target = true;
				break;
			case "off":
				target = false;
				break;
			case "toggle":
				target = !target;
				break;
			default:
				Services.Chat.PrintError(
					$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
				break;
		}
	}

	public void HydrateOverlays()
	{
		// Hydrate any overlays in the config
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (!overlayConfig.Disabled && (!overlayConfig.ActOptimizations || _actAvailable))
			{
				OverlayAdded?.Invoke(this, overlayConfig);
			}
		}
	}

	private void NavigateOverlay(InlayConfiguration overlayConfig)
	{
		if (overlayConfig.Url == "") { overlayConfig.Url = "about:blank"; }

		OverlayNavigated?.Invoke(this, overlayConfig);
	}

	private void UpdateZoomOverlay(InlayConfiguration overlayConfig)
	{
		OverlayZoomed?.Invoke(this, overlayConfig);
	}

	private void UpdateMuteOverlay(InlayConfiguration overlayConfig)
	{
		OverlayMuted?.Invoke(this, overlayConfig);
	}

	private void UpdateUserCss(InlayConfiguration overlayConfig)
	{
		OverlayUserCssChanged?.Invoke(this, overlayConfig);
	}

	private void ReloadOverlay(InlayConfiguration overlayConfig) { NavigateOverlay(overlayConfig); }

	private void DebugOverlay(InlayConfiguration overlayConfig)
	{
		OverlayDebugged?.Invoke(this, overlayConfig);
	}

	private void DebouncedSaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
	}

	private void SaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = null;
		Services.PluginInterface.SavePluginConfig(Config);
	}

	private string GetOverlayCommandName(InlayConfiguration overlayConfig)
	{
		return Regex.Replace(overlayConfig.Name, @"\s+", "").ToLower();
	}

	public override void Draw()
	{
		RenderPaneSelector();

		// Pane details
		bool dirty = false;
		ImGui.SameLine();
		ImGui.BeginChild("details");
		if (_selectedOverlay == null)
		{
			dirty |= RenderGeneralSettings();
		}
		else
		{
			dirty |= RenderOverlaySettings(_selectedOverlay);
		}

		ImGui.EndChild();

		if (dirty) { DebouncedSaveSettings(); }

	}

	private void RenderPaneSelector()
	{
		ImGui.BeginGroup();
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

		const int selectorWidth = 110;
		ImGui.BeginChild("panes", new Vector2(selectorWidth, -1), true);

		if (ImGui.Selectable("General", _selectedOverlay == null))
		{
			_selectedOverlay = null;
		}

		ImGui.Dummy(new Vector2(0, 5));
		InlayConfiguration? primaryOverlay = PrimaryOverlay;
		if (primaryOverlay is not null && ImGui.Selectable("T3 Code", _selectedOverlay == primaryOverlay))
		{
			_selectedOverlay = primaryOverlay;
		}

		ImGui.EndChild();
		ImGui.PopStyleVar();
		ImGui.EndGroup();
	}

	private bool RenderGeneralSettings()
	{
		bool dirty = false;

		dirty |= ImGui.Checkbox("Show floating T3 bubble", ref Config.ShowBubble);
		ImGui.TextWrapped("Click the bubble to show or hide T3 Code. Select the T3 Code overlay on the left to change its URL, size, zoom, or frame rate.");
		T3ConnectionStatus? connection = T3ConnectionStatusProvider?.Invoke();
		if (connection is not null)
		{
			string state = connection.IsReachable switch
			{
				true => "Connected",
				false => "Not reachable",
				_ => "Checking",
			};
			ImGui.Text($"T3 Code: {state} — {connection.Url}");
			if (connection.IsReachable == false)
			{
				ImGui.TextWrapped("Start T3 Code, or select the T3 Code overlay on the left and correct its URL.");
				if (ImGui.Button("Retry connection")) RetryT3Connection?.Invoke();
			}
		}

		ImGui.Dummy(new Vector2(0, 8));
		ImGui.Separator();
		ImGui.Text("Window behaviour");
		dirty |= ImGui.Checkbox("Animate opening and closing", ref Config.AnimateVisibility);
		dirty |= ImGui.Checkbox("Dim when inactive", ref Config.DimWhenInactive);

		if (!Config.DimWhenInactive) ImGui.BeginDisabled();
		if (ImGui.SliderFloat("Idle opacity", ref Config.IdleOpacity, 10f, 90f, "%.0f%%"))
		{
			Config.IdleOpacity = Math.Clamp(Config.IdleOpacity, 10f, 90f);
			dirty = true;
		}
		if (!Config.DimWhenInactive) ImGui.EndDisabled();
		ImGui.TextWrapped("T3 stays solid while hovered or focused, then dims after you click back into the game.");

		ImGui.Dummy(new Vector2(0, 8));
		ImGui.Separator();
		ImGui.Text("Thread notifications");
		if (ImGui.InputTextWithHint("T3 data directory", "Automatic", ref Config.T3DataDirectory, 1000))
		{
			dirty = true;
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
		{
			T3DataDirectoryChanged?.Invoke(this, Config.T3DataDirectory);
		}
		string? databasePath = T3DatabasePathProvider?.Invoke();
		ImGui.TextWrapped(databasePath is null
			? "T3 thread database not found. The overlay still works; set the folder containing state.sqlite if you want notification badges."
			: $"Using: {databasePath}");

		if (ImGui.CollapsingHeader("Commands"))
		{
			ImGui.Text("/t3");
			ImGui.TextWrapped("Show or hide the T3 Code window.");
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.Text("/t3 show");
			ImGui.TextWrapped("Show the T3 Code window.");
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.Text("/t3 config");
			ImGui.TextWrapped("Open this configuration window.");
		}

		return dirty;
	}

	private bool RenderOverlaySettings(InlayConfiguration overlayConfig)
	{
		bool dirty = false;

		ImGui.PushID(overlayConfig.Guid.ToString());
		ImGui.Text("T3 Code browser");

		dirty |= ImGui.InputText("URL", ref overlayConfig.Url, 1000);
		if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateOverlay(overlayConfig); }

		if (ImGui.InputFloat("Zoom", ref overlayConfig.Zoom, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Zoom < 10f)
			{
				overlayConfig.Zoom = 10f;
			}
			else if (overlayConfig.Zoom > 500f)
			{
				overlayConfig.Zoom = 500f;
			}

			dirty = true;

			// notify of zoom change
			UpdateZoomOverlay(overlayConfig);
		}

		if (ImGui.InputFloat("Opacity", ref overlayConfig.Opacity, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Opacity < 10f)
			{
				overlayConfig.Opacity = 10f;
			}
			else if (overlayConfig.Opacity > 100f)
			{
				overlayConfig.Opacity = 100f;
			}

			dirty = true;
		}

		if (ImGui.InputInt("Framerate", ref overlayConfig.Framerate, 1, 10))
		{
			// clamp to allowed range 
			if (overlayConfig.Framerate < 1)
			{
				overlayConfig.Framerate = 1;
			}
			else if (overlayConfig.Framerate > 300)
			{
				overlayConfig.Framerate = 300;
			}

			dirty = true;

			// framerate changes require the recreation of the browser instance
			// TODO: this is ugly as heck, fix once proper IPC is up and running
			OverlayRemoved?.Invoke(this, overlayConfig);
			OverlayAdded?.Invoke(this, overlayConfig);
		}

		if (ImGui.Checkbox("Muted", ref overlayConfig.Muted))
		{
			UpdateMuteOverlay(overlayConfig);
			dirty = true;
		}
		ImGui.SameLine();
		dirty |= ImGui.Checkbox("Hidden", ref overlayConfig.Hidden);

		ImGui.NewLine();
		if (ImGui.CollapsingHeader("Advanced"))
		{
			ImGui.Text("Custom CSS code:");
			if (ImGui.InputTextMultiline("Custom CSS code", ref overlayConfig.CustomCss, 1000000,
				    new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
			{
				dirty = true;
			}

			if (ImGui.IsItemDeactivatedAfterEdit()) { UpdateUserCss(overlayConfig); }
		}

		ImGui.NewLine();
		if (ImGui.Button("Reload")) { ReloadOverlay(overlayConfig); }

		ImGui.SameLine();
		if (ImGui.Button("Open Dev Tools")) { DebugOverlay(overlayConfig); }

		ImGui.PopID();

		return dirty;
	}
}
