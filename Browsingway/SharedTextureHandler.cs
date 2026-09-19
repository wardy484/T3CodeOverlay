using Dalamud.Bindings.ImGui;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Browsingway;

internal unsafe class SharedTextureHandler : IDisposable
{
	private readonly ID3D11ShaderResourceView* _view;
	private readonly ID3D11Texture2D* _texture;
	private readonly Vector2 _size;
	private readonly ImTextureID _textureId;

	public SharedTextureHandler(IntPtr handle)
	{
		ID3D11Device* device = DxHandler.Device;
		if (device == null)
		{
			throw new Exception("Device is null");
		}

		// Open the shared resource
		Guid texture2DGuid = typeof(ID3D11Texture2D).GUID;
		void* texturePtr;
		HRESULT hr = device->OpenSharedResource((HANDLE)handle, &texture2DGuid, &texturePtr);
		if (hr.FAILED)
		{
			throw new Exception($"Could not open shared resource: {hr}");
		}

		_texture = (ID3D11Texture2D*)texturePtr;

		// Get the texture description
		D3D11_TEXTURE2D_DESC texDesc;
		_texture->GetDesc(&texDesc);

		// Create the shader resource view
		D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = new()
		{
			Format = texDesc.Format, ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D, Texture2D = new D3D11_TEX2D_SRV {MostDetailedMip = 0, MipLevels = texDesc.MipLevels}
		};

		ID3D11ShaderResourceView* view;
		hr = device->CreateShaderResourceView((ID3D11Resource*)_texture, &srvDesc, &view);
		if (hr.FAILED)
		{
			_texture->Release();
			throw new Exception($"Could not create shader resource view: {hr}");
		}

		_view = view;
		_size = new Vector2(texDesc.Width, texDesc.Height);
		_textureId = new ImTextureID((nint)_view);
	}

	public void Dispose()
	{
		_view->Release();
		_texture->Release();
	}

	public void Render(float cornerRadius = 0f, Vector2? animationTarget = null, float animationProgress = 1f)
	{
		Vector2 min = ImGui.GetCursorScreenPos();
		if (animationTarget is { } target && animationProgress < 1f)
		{
			RenderSlurp(min, target, animationProgress, cornerRadius);
			ImGui.Dummy(_size);
			return;
		}

		if (cornerRadius <= 0f)
		{
			ImGui.Image(_textureId, _size);
			return;
		}

		ImGui.GetWindowDrawList().AddImageRounded(
			_textureId,
			min,
			min + _size,
			Vector2.Zero,
			Vector2.One,
			ImGui.GetColorU32(Vector4.One),
			cornerRadius);
		ImGui.Dummy(_size);
	}

	private void RenderSlurp(Vector2 min, Vector2 target, float progress, float cornerRadius)
	{
		const int columns = 20;
		const int cornerSegments = 8;
		const int rowBoundaries = cornerSegments * 2 + 2;
		float eased = SmootherStep(progress);
		bool targetIsLeft = target.X < min.X + _size.X / 2f;
		ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
		uint tint = ImGui.GetColorU32(Vector4.One);

		for (int row = 0; row < rowBoundaries - 1; row++)
		{
			float y0 = GetRoundedRowY(row, rowBoundaries, cornerSegments, cornerRadius);
			float y1 = GetRoundedRowY(row + 1, rowBoundaries, cornerSegments, cornerRadius);
			float v0 = y0 / _size.Y;
			float v1 = y1 / _size.Y;
			float inset0 = GetCornerInset(y0, cornerRadius) / _size.X;
			float inset1 = GetCornerInset(y1, cornerRadius) / _size.X;

			for (int column = 0; column < columns; column++)
			{
				float column0 = column / (float)columns;
				float column1 = (column + 1f) / columns;
				float topU0 = float.Lerp(inset0, 1f - inset0, column0);
				float topU1 = float.Lerp(inset0, 1f - inset0, column1);
				float bottomU0 = float.Lerp(inset1, 1f - inset1, column0);
				float bottomU1 = float.Lerp(inset1, 1f - inset1, column1);

				Vector2 top0 = GetSlurpedPoint(min, target, eased, targetIsLeft, topU0, v0);
				Vector2 top1 = GetSlurpedPoint(min, target, eased, targetIsLeft, topU1, v0);
				Vector2 bottom0 = GetSlurpedPoint(min, target, eased, targetIsLeft, bottomU0, v1);
				Vector2 bottom1 = GetSlurpedPoint(min, target, eased, targetIsLeft, bottomU1, v1);
				drawList.AddImageQuad(
					_textureId,
					top0,
					top1,
					bottom1,
					bottom0,
					new Vector2(topU0, v0),
					new Vector2(topU1, v0),
					new Vector2(bottomU1, v1),
					new Vector2(bottomU0, v1),
					tint);
			}
		}
	}

	private Vector2 GetSlurpedPoint(Vector2 min, Vector2 target, float progress, bool targetIsLeft, float u, float v)
	{
		float distanceFromTarget = targetIsLeft ? u : 1f - u;
		float bend = 1f - 0.45f * (1f - progress) * distanceFromTarget;
		float scale = progress * bend;
		Vector2 point = min + new Vector2(_size.X * u, _size.Y * v);
		return Vector2.Lerp(target, point, scale);
	}

	private float GetRoundedRowY(int row, int rowBoundaries, int cornerSegments, float cornerRadius)
	{
		if (row <= cornerSegments)
		{
			return cornerRadius * row / cornerSegments;
		}

		return _size.Y - cornerRadius + cornerRadius * (row - cornerSegments - 1) / cornerSegments;
	}

	private float GetCornerInset(float y, float cornerRadius)
	{
		float distanceFromEdge = MathF.Min(y, _size.Y - y);
		if (distanceFromEdge >= cornerRadius) return 0f;

		float radiusOffset = cornerRadius - distanceFromEdge;
		return cornerRadius - MathF.Sqrt(cornerRadius * cornerRadius - radiusOffset * radiusOffset);
	}

	private static float SmootherStep(float value)
	{
		value = Math.Clamp(value, 0f, 1f);
		return value * value * value * (value * (value * 6f - 15f) + 10f);
	}
}
