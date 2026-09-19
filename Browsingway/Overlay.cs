using Browsingway.Common.Ipc;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace Browsingway;

internal class Overlay : IDisposable
{
	private const float WindowCornerRadius = 16f;
	private const float VisibilityAnimationSeconds = 0.28f;
	private const float InteractionFadeSeconds = 0.16f;

	private readonly InlayConfiguration _overlayConfig;
	private readonly Configuration _pluginConfig;

	private readonly RenderProcess _renderProcess;
	private bool _captureCursor;
	private ImGuiMouseCursor _cursor;

	private bool _mouseInWindow;

	private bool _resizing;
	private Vector2 _size;
	private bool _hasRenderError = false;
	private SharedTextureHandler? _textureHandler;
	private Exception? _textureRenderException;
	private bool _windowFocused;
	private bool _windowHovered;
	private bool _bubbleHovered;
	private bool _bubbleFocused;
	private float _interactionOpacity;
	private Vector2? _bubbleAnchor;
	private float _bubbleSize;
	private Vector2 _windowSize;
	private long _timeLastInCombat;
	private ISharedImmediateTexture? _texErrorIcon;
	private float _visibilityProgress;

	public Overlay(RenderProcess renderProcess, InlayConfiguration overlayConfig, Configuration pluginConfig, string pluginDir)
	{
		_renderProcess = renderProcess;
		// TODO: handle that the correct way
		_renderProcess.Crashed += (_, _) =>
		{
			_size = Vector2.Zero;
			_hasRenderError = true;
		};

		_overlayConfig = overlayConfig;
		_pluginConfig = pluginConfig;
		_visibilityProgress = overlayConfig.Hidden ? 0f : 1f;
		_interactionOpacity = GetIdleOpacity();
		_texErrorIcon = Services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "dead.png"));
	}

	public Guid RenderGuid => _overlayConfig.Guid;

	public void SetBubbleAnchor(Vector2? position, float bubbleSize)
	{
		_bubbleAnchor = position;
		_bubbleSize = bubbleSize;
	}

	public void SetBubbleHovered(bool hovered)
	{
		_bubbleHovered = hovered;
	}

	public void FocusFromBubble()
	{
		_bubbleFocused = true;
	}

	public void Dispose()
	{
		_textureHandler?.Dispose();
		_ = _renderProcess.Rpc?.RemoveOverlay(RenderGuid);
	}

	public void Navigate(string newUrl)
	{
		_ = _renderProcess.Rpc?.Navigate(RenderGuid, newUrl);
	}

	public void InjectUserCss(string css)
	{
		_ = _renderProcess.Rpc?.InjectUserCss(RenderGuid, css);
	}

	public void Zoom(float zoom)
	{
		_ = _renderProcess.Rpc?.Zoom(RenderGuid, zoom);
	}

	public void Mute(bool mute)
	{
		_ = _renderProcess.Rpc?.Mute(RenderGuid, mute);
	}

	public void Debug()
	{
		_ = _renderProcess.Rpc?.Debug(RenderGuid);
	}

	public void SetCursor(Cursor cursor)
	{
		_captureCursor = cursor != Cursor.BrowsingwayNoCapture;
		_cursor = DecodeCursor(cursor);
	}

	public (bool, long) WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
	{
		if (msg == WindowsMessage.WM_LBUTTONDOWN)
		{
			// this message is only generated when someone clicked on an non ImGui window, meaning we want to loose focus here
			_windowFocused = false;
			_bubbleFocused = false;
		}

		// Bail if we're not focused or we're typethrough
		// TODO: Revisit the focus check for UI stuff, might not hold
		if (!_windowFocused || _overlayConfig.TypeThrough) { return (false, 0); }

		KeyEventType? eventType = msg switch
		{
			WindowsMessage.WM_KEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_KEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_CHAR => KeyEventType.Character,
			WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
			_ => null
		};

		// If the event isn't something we're tracking, bail early with no capture
		if (eventType == null) { return (false, 0); }

		_ = _renderProcess.Rpc?.KeyEvent(RenderGuid, (int)msg, (int)wParam, (int)lParam);

		// We've handled the input, signal. For these message types, `0` signals a capture.
		return (true, 0);
	}

	public void Render()
	{
		if (_overlayConfig.Disabled || HiddenByCombatFlags() ||
		    (_overlayConfig.HideInPvP && Services.ClientState.IsPvP))
		{
			_mouseInWindow = false;
			_windowHovered = false;
			return;
		}

		UpdateVisibilityAnimation();
		if (_visibilityProgress <= 0f)
		{
			_mouseInWindow = false;
			_windowHovered = false;
			return;
		}

		float visibility = SmoothStep(_visibilityProgress);
		UpdateInteractionOpacity();

		ImGui.SetNextWindowSize(new Vector2(960, 720), ImGuiCond.FirstUseEver);
		if (_bubbleAnchor is { } bubblePosition && !_overlayConfig.Fullscreen)
		{
			ImGui.SetNextWindowPos(GetAnchoredPosition(bubblePosition), ImGuiCond.Always);
		}
		float cornerRadius = _overlayConfig.Fullscreen ? 0f : WindowCornerRadius;
		float fade = Math.Clamp(visibility / 0.16f, 0f, 1f);
		float interactionOpacity = _overlayConfig.Fullscreen ? 1f : _interactionOpacity;
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, fade * interactionOpacity * (_overlayConfig.Opacity / 100f));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, cornerRadius);
		ImGui.Begin($"{_overlayConfig.Name}###{_overlayConfig.Guid}", GetWindowFlags(_visibilityProgress < 1f));
		ImGui.PopStyleVar(1);
		_windowSize = ImGui.GetWindowSize();
		Vector2 mousePosition = ImGui.GetIO().MousePos;
		Vector2 windowPosition = ImGui.GetWindowPos();
		_windowHovered = !_overlayConfig.ClickThrough
		                 && mousePosition.X >= windowPosition.X
		                 && mousePosition.Y >= windowPosition.Y
		                 && mousePosition.X < windowPosition.X + _windowSize.X
		                 && mousePosition.Y < windowPosition.Y + _windowSize.Y;

		if (_overlayConfig.Fullscreen)
		{
			var screen = ImGui.GetMainViewport();

			// ImGui always leaves a 1px transparent border around the window, so we need to account for that.
			var fsPos = new Vector2(screen.WorkPos.X - 1, screen.WorkPos.Y - 1);
			var fsSize = new Vector2(screen.Size.X + 2 - fsPos.X, screen.Size.Y + 2 - fsPos.Y);

			if (ImGui.GetWindowPos() != fsPos)
			{
				ImGui.SetWindowPos(fsPos, ImGuiCond.Always);
			}

			if (_size.X != fsSize.X || _size.Y != fsSize.Y)
			{
				ImGui.SetWindowSize(fsSize, ImGuiCond.Always);
			}
		}

		HandleWindowSize();

		// TODO: Browsingway.Renderer can take some time to spin up properly, should add a loading state.
		if (_textureHandler != null && !_hasRenderError)
		{
			HandleMouseEvent();

			Vector2? animationTarget = _bubbleAnchor is { } anchor && !_overlayConfig.Fullscreen
				? anchor + new Vector2(_bubbleSize / 2f)
				: null;
			_textureHandler.Render(cornerRadius, animationTarget, _visibilityProgress);
		}
		else
		{
			if (_texErrorIcon is not null)
			{
				float lineHeight = ImGui.GetTextLineHeight();
				float size = float.Min(_size.X - lineHeight * 3, _size.Y - lineHeight * 3);
				ImGui.NewLine();
				ImGuiHelpers.CenterCursorFor(size);
				ImGui.Image(_texErrorIcon.GetWrapOrEmpty().Handle, new Vector2(size, size));

				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				if (_textureRenderException is not null)
				{
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture:");
					ImGuiHelpers.CenteredText(_textureRenderException.ToString());
				}
				else
				{
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture. Check the log for more details.");
				}

				ImGui.PopStyleColor();
			}
		}

		ImGui.End();
		ImGui.PopStyleVar();
	}

	private void UpdateVisibilityAnimation()
	{
		if (!_pluginConfig.AnimateVisibility)
		{
			_visibilityProgress = _overlayConfig.Hidden ? 0f : 1f;
			return;
		}

		float direction = _overlayConfig.Hidden ? -1f : 1f;
		_visibilityProgress = Math.Clamp(
			_visibilityProgress + direction * ImGui.GetIO().DeltaTime / VisibilityAnimationSeconds,
			0f,
			1f);
	}

	private void UpdateInteractionOpacity()
	{
		float idleOpacity = GetIdleOpacity();
		float target = !_pluginConfig.DimWhenInactive || _windowFocused || _windowHovered || _bubbleHovered || _bubbleFocused
			? 1f
			: idleOpacity;
		float step = (1f - idleOpacity) * ImGui.GetIO().DeltaTime / InteractionFadeSeconds;
		_interactionOpacity = target > _interactionOpacity
			? MathF.Min(target, _interactionOpacity + step)
			: MathF.Max(target, _interactionOpacity - step);
	}

	private float GetIdleOpacity() => Math.Clamp(_pluginConfig.IdleOpacity / 100f, 0.1f, 1f);

	private static float SmoothStep(float value) => value * value * (3f - 2f * value);

	private Vector2 GetAnchoredPosition(Vector2 bubblePosition)
	{
		const float gap = 10f;
		Vector2 panelSize = _windowSize == Vector2.Zero ? new Vector2(960, 720) : _windowSize;
		ImGuiViewportPtr viewport = ImGui.GetMainViewport();
		Vector2 workMin = viewport.WorkPos;
		Vector2 workMax = viewport.WorkPos + viewport.WorkSize;
		float bubbleCentre = bubblePosition.X + _bubbleSize / 2f;
		float screenCentre = workMin.X + viewport.WorkSize.X / 2f;
		float x = bubbleCentre > screenCentre
			? bubblePosition.X - panelSize.X - gap
			: bubblePosition.X + _bubbleSize + gap;
		float y = bubblePosition.Y - 14f;

		return new Vector2(
			Math.Clamp(x, workMin.X, Math.Max(workMin.X, workMax.X - panelSize.X)),
			Math.Clamp(y, workMin.Y, Math.Max(workMin.Y, workMax.Y - panelSize.Y)));
	}

	private ImGuiWindowFlags GetWindowFlags(bool animating = false)
	{
		ImGuiWindowFlags flags = ImGuiWindowFlags.None
		                         | ImGuiWindowFlags.NoTitleBar
		                         | ImGuiWindowFlags.NoCollapse
		                         | ImGuiWindowFlags.NoScrollbar
		                         | ImGuiWindowFlags.NoScrollWithMouse
		                         | ImGuiWindowFlags.NoBringToFrontOnFocus
		                         | ImGuiWindowFlags.NoFocusOnAppearing;

		// ClickThrough / fullscreen is implicitly locked
		bool locked = _overlayConfig.Locked || _overlayConfig.ClickThrough || _overlayConfig.Fullscreen;

		if (locked)
		{
			flags |= ImGuiWindowFlags.None
			         | ImGuiWindowFlags.NoMove
			         | ImGuiWindowFlags.NoResize
			         | ImGuiWindowFlags.NoBackground;
		}

		if (_overlayConfig.ClickThrough || (!_captureCursor && locked))
		{
			flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav;
		}

		if (animating)
		{
			flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoBackground;
		}

		// don't think user wants a background when they decrease opacity
		if (_overlayConfig.Opacity < 100f)
			flags |= ImGuiWindowFlags.NoBackground;

		return flags;
	}

	public void SetTexture(IntPtr handle)
	{
		_resizing = false;
		_hasRenderError = false;

		SharedTextureHandler? oldTextureHandler = _textureHandler;
		try
		{
			_textureHandler = new SharedTextureHandler(handle);
		}
		catch (Exception e) { _textureRenderException = e; }

		if (oldTextureHandler != null) { oldTextureHandler.Dispose(); }
	}

	private void HandleMouseEvent()
	{
		// Render proc won't be ready on first boot
		// Totally skip mouse handling for click through overlays, as well
		if (_renderProcess == null || _overlayConfig.ClickThrough) { return; }

		ImGuiIOPtr io = ImGui.GetIO();
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 mousePos = io.MousePos - windowPos - ImGui.GetWindowContentRegionMin();

		// Generally we want to use IsWindowHovered for hit checking, as it takes z-stacking into account -
		// but when cursor isn't being actively captured, imgui will always return false - so fall back
		// so a slightly more naive hover check, just to maintain a bit of flood prevention.
		// TODO: Need to test how this will handle overlaps... fully transparent _shouldn't_ be accepting
		//       clicks so shouuulllddd beee fineee???
		bool hovered = _captureCursor
			? ImGui.IsWindowHovered()
			: ImGui.IsMouseHoveringRect(windowPos, windowPos + ImGui.GetWindowSize());

		// manage focus
		MouseButton down = EncodeMouseButtons(io.MouseClicked);
		MouseButton double_ = EncodeMouseButtons(io.MouseDoubleClicked);
		MouseButton up = EncodeMouseButtons(io.MouseReleased);
		float wheelX = io.MouseWheelH;
		float wheelY = io.MouseWheel;
		if (down.HasFlag(MouseButton.Primary) || down.HasFlag(MouseButton.Secondary) || down.HasFlag(MouseButton.Tertiary))
		{
			_windowFocused = hovered;
			if (hovered) _bubbleFocused = false;
		}

		// If the cursor is outside the window, send a final mouse leave then noop
		if (!hovered)
		{
			if (_mouseInWindow)
			{
				_mouseInWindow = false;
				_ = _renderProcess.Rpc?.MouseButton(new MouseButtonMessage() {Guid = RenderGuid.ToByteArray(), X = (int)mousePos.X, Y = (int)mousePos.Y, Leaving = true});
			}

			return;
		}

		_mouseInWindow = true;

		ImGui.SetMouseCursor(_cursor);

		// If the event boils down to no change, bail before sending
		if (io.MouseDelta == Vector2.Zero && down == MouseButton.None && double_ == MouseButton.None && up == MouseButton.None && wheelX == 0 && wheelY == 0)
		{
			return;
		}

		InputModifier modifier = InputModifier.None;
		if (io.KeyShift) { modifier |= InputModifier.Shift; }

		if (io.KeyCtrl) { modifier |= InputModifier.Control; }

		if (io.KeyAlt) { modifier |= InputModifier.Alt; }

		// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
		_ = _renderProcess.Rpc?.MouseButton(new MouseButtonMessage()
		{
			Guid = RenderGuid.ToByteArray(),
			X = mousePos.X,
			Y = mousePos.Y,
			Down = down,
			Double = double_,
			Up = up,
			WheelX = wheelX,
			WheelY = wheelY,
			Modifier = modifier
		});
	}

	private void HandleWindowSize()
	{
		Vector2 currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
		if (currentSize == _size || _resizing) { return; }

		if (_size == Vector2.Zero)
		{
			_ = _renderProcess.Rpc?.NewOverlay(new NewOverlayMessage()
			{
				Guid = RenderGuid.ToByteArray(),
				Id = _overlayConfig.Name,
				Url = _overlayConfig.Url,
				Width = (int)currentSize.X,
				Height = (int)currentSize.Y,
				Zoom = _overlayConfig.Zoom,
				Framerate = _overlayConfig.Framerate,
				Muted = _overlayConfig.Muted,
				CustomCss = _overlayConfig.CustomCss
			});
		}
		else
		{
			_ = _renderProcess.Rpc?.ResizeOverlay(RenderGuid, (int)currentSize.X, (int)currentSize.Y);
		}

		_resizing = true;
		_size = currentSize;
	}

	#region serde

	private MouseButton EncodeMouseButtons(Span<bool> buttons)
	{
		MouseButton result = MouseButton.None;
		if (buttons[0]) { result |= MouseButton.Primary; }

		if (buttons[1]) { result |= MouseButton.Secondary; }

		if (buttons[2]) { result |= MouseButton.Tertiary; }

		if (buttons[3]) { result |= MouseButton.Fourth; }

		if (buttons[4]) { result |= MouseButton.Fifth; }

		return result;
	}

	private ImGuiMouseCursor DecodeCursor(Cursor cursor)
	{
		// ngl kinda disappointed at the lack of options here
		switch (cursor)
		{
			case Cursor.Default: return ImGuiMouseCursor.Arrow;
			case Cursor.None: return ImGuiMouseCursor.None;
			case Cursor.Pointer: return ImGuiMouseCursor.Hand;

			case Cursor.Text:
			case Cursor.VerticalText:
				return ImGuiMouseCursor.TextInput;

			case Cursor.NResize:
			case Cursor.SResize:
			case Cursor.NsResize:
				return ImGuiMouseCursor.ResizeNs;

			case Cursor.EResize:
			case Cursor.WResize:
			case Cursor.EwResize:
				return ImGuiMouseCursor.ResizeEw;

			case Cursor.NeResize:
			case Cursor.SwResize:
			case Cursor.NeswResize:
				return ImGuiMouseCursor.ResizeNesw;

			case Cursor.NwResize:
			case Cursor.SeResize:
			case Cursor.NwseResize:
				return ImGuiMouseCursor.ResizeNwse;
		}

		return ImGuiMouseCursor.Arrow;
	}

	private bool HiddenByCombatFlags()
	{
		if (!_overlayConfig.HideOutOfCombat)
		{
			return false;
		}

		if (Services.ObjectTable.LocalPlayer == null)
		{
			return true;
		}

		if (Services.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat))
		{
			_timeLastInCombat = DateTimeOffset.Now.ToUnixTimeMilliseconds();
			return false;
		}

		if (!Services.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat) && _overlayConfig.HideDelay > 0)
		{
			return DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _timeLastInCombat + (_overlayConfig.HideDelay * 1000);
		}

		return true;
	}

	#endregion
}
