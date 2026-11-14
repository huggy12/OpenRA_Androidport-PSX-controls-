#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Default
{
	sealed class Texture : ThreadAffine, ITextureInternal
	{
		uint texture;
		TextureScaleFilter scaleFilter;

		public uint ID => texture;
		public Size Size { get; private set; }

		bool disposed;

		public TextureScaleFilter ScaleFilter
		{
			get => scaleFilter;

			set
			{
				VerifyThreadAffinity();
				if (scaleFilter == value)
					return;

				scaleFilter = value;
				PrepareTexture();
			}
		}

		public Texture()
		{
			OpenGL.glGenTextures(1, out texture);
			OpenGL.CheckGLError();
		}

		void PrepareTexture()
		{
			OpenGL.CheckGLError();
			OpenGL.glBindTexture(OpenGL.GL_TEXTURE_2D, texture);
			OpenGL.CheckGLError();

			var filter = scaleFilter == TextureScaleFilter.Linear ? OpenGL.GL_LINEAR : OpenGL.GL_NEAREST;
			OpenGL.glTexParameteri(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, filter);
			OpenGL.CheckGLError();
			OpenGL.glTexParameteri(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, filter);
			OpenGL.CheckGLError();

			OpenGL.glTexParameterf(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
			OpenGL.CheckGLError();
			OpenGL.glTexParameterf(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
			OpenGL.CheckGLError();

			OpenGL.glTexParameteri(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_BASE_LEVEL, 0);
			OpenGL.CheckGLError();
			OpenGL.glTexParameteri(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAX_LEVEL, 0);
			OpenGL.CheckGLError();
		}

		static int InternalFormat => OpenGL.RequiresBGRASwizzle ? OpenGL.GL_RGBA :
			OpenGL.Profile == GLProfile.Embedded ? OpenGL.GL_BGRA : OpenGL.GL_RGBA8;

		/// <summary>Copies a BGRA region (with the given source row stride in pixels) into a tightly packed RGBA array.</summary>
		static byte[] SwizzleToRGBA(IntPtr data, int stride, int xoffset, int yoffset, int width, int height)
		{
			return CopyRegion(data, stride, xoffset, yoffset, width, height, true);
		}

		/// <summary>Copies a region (with the given source row stride in pixels) into a tightly packed array, optionally swapping BGRA to RGBA.</summary>
		static unsafe byte[] CopyRegion(IntPtr data, int stride, int xoffset, int yoffset, int width, int height, bool swizzle)
		{
			if (!swizzle)
			{
				var packed = new byte[4 * width * height];
				var source = (byte*)data;
				for (var y = 0; y < height; y++)
					Marshal.Copy(new IntPtr(source + 4 * ((yoffset + y) * stride + xoffset)), packed, 4 * y * width, 4 * width);

				return packed;
			}

			var result = new byte[4 * width * height];
			var src = (byte*)data;
			for (var y = 0; y < height; y++)
			{
				var srcRow = src + 4 * ((yoffset + y) * stride + xoffset);
				var dst = 4 * y * width;
				for (var x = 0; x < width; x++)
				{
					result[dst++] = srcRow[2];
					result[dst++] = srcRow[1];
					result[dst++] = srcRow[0];
					result[dst++] = srcRow[3];
					srcRow += 4;
				}
			}

			return result;
		}

		void SetData(IntPtr data, int width, int height)
		{
			PrepareTexture();
			if (OpenGL.RequiresBGRASwizzle)
			{
				if (data == IntPtr.Zero)
					OpenGL.glTexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, width, height,
						0, OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, IntPtr.Zero);
				else
				{
					var rgba = SwizzleToRGBA(data, width, 0, 0, width, height);
					unsafe
					{
						fixed (byte* ptr = &rgba[0])
							OpenGL.glTexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, width, height,
								0, OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, new IntPtr(ptr));
					}
				}

				OpenGL.CheckGLError();
				return;
			}

			OpenGL.glTexImage2D(OpenGL.GL_TEXTURE_2D, 0, InternalFormat, width, height,
				0, OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, data);
			OpenGL.CheckGLError();
		}

		public void SetData(byte[] colors, int width, int height)
		{
			VerifyThreadAffinity();
			if (!Exts.IsPowerOf2(width) || !Exts.IsPowerOf2(height))
				throw new InvalidDataException($"Non-power-of-two array {width}x{height}");

			Size = new Size(width, height);
			unsafe
			{
				fixed (byte* ptr = &colors[0])
					SetData(new IntPtr(ptr), width, height);
			}
		}

		void SetSubData(IntPtr data, int xoffset, int yoffset, int width, int height)
		{
			PrepareTexture();

			// Some mobile GPU drivers (e.g. Adreno) corrupt sub-region uploads that use the UNPACK_ROW_LENGTH /
			// SKIP_PIXELS / SKIP_ROWS state, so on Android copy the region into a tightly packed buffer instead.
			if (OpenGL.RequiresBGRASwizzle || Platform.IsAndroid)
			{
				var swizzle = OpenGL.RequiresBGRASwizzle;
				var packed = CopyRegion(data, Size.Width, xoffset, yoffset, width, height, swizzle);
				unsafe
				{
					fixed (byte* ptr = &packed[0])
						OpenGL.glTexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, xoffset, yoffset, width, height,
							swizzle ? OpenGL.GL_RGBA : OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, new IntPtr(ptr));
				}

				OpenGL.CheckGLError();
				return;
			}

			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_ROW_LENGTH, Size.Width);
			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_SKIP_PIXELS, xoffset);
			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_SKIP_ROWS, yoffset);

			OpenGL.glTexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, xoffset, yoffset, width, height,
				OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, data);

			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_ROW_LENGTH, 0);
			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_SKIP_PIXELS, 0);
			OpenGL.glPixelStorei(OpenGL.GL_UNPACK_SKIP_ROWS, 0);

			OpenGL.CheckGLError();
		}

		public void SetSubData(byte[] colors, int xoffset, int yoffset, int width, int height)
		{
			VerifyThreadAffinity();
			unsafe
			{
				fixed (byte* ptr = &colors[0])
					SetSubData(new IntPtr(ptr), xoffset, yoffset, width, height);
			}
		}

		public void SetFloatData(float[] data, int width, int height)
		{
			VerifyThreadAffinity();
			if (!Exts.IsPowerOf2(width) || !Exts.IsPowerOf2(height))
				throw new InvalidDataException($"Non-power-of-two array {width}x{height}");

			Size = new Size(width, height);
			unsafe
			{
				fixed (float* ptr = &data[0])
				{
					PrepareTexture();
					OpenGL.glTexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA16F, width, height,
						0, OpenGL.GL_RGBA, OpenGL.GL_FLOAT, new IntPtr(ptr));
					OpenGL.CheckGLError();
				}
			}
		}

		public void SetDataFromReadBuffer(Rectangle rect)
		{
			VerifyThreadAffinity();
			if (!Exts.IsPowerOf2(rect.Width) || !Exts.IsPowerOf2(rect.Height))
				throw new InvalidDataException($"Non-power-of-two rectangle {rect.Width}x{rect.Height}");

			PrepareTexture();

			// GLES only accepts the core formats (not BGRA) for glCopyTexImage2D; Mali GPUs reject BGRA with an error
			var glInternalFormat = OpenGL.Profile == GLProfile.Embedded ? OpenGL.GL_RGBA : OpenGL.GL_RGBA8;
			OpenGL.glCopyTexImage2D(OpenGL.GL_TEXTURE_2D, 0, glInternalFormat, rect.X, rect.Y, rect.Width, rect.Height, 0);
			OpenGL.CheckGLError();
		}

		public byte[] GetData()
		{
			VerifyThreadAffinity();
			var data = new byte[4 * Size.Width * Size.Height];

			// GLES doesn't support glGetTexImage so data must be read back via a frame buffer
			if (OpenGL.Profile == GLProfile.Embedded)
			{
				// Query the active framebuffer so we can restore it afterwards
				OpenGL.glGetIntegerv(OpenGL.GL_FRAMEBUFFER_BINDING, out var lastFramebuffer);

				OpenGL.glGenFramebuffers(1, out var framebuffer);
				OpenGL.glBindFramebuffer(OpenGL.GL_FRAMEBUFFER, framebuffer);
				OpenGL.CheckGLError();

				OpenGL.glFramebufferTexture2D(OpenGL.GL_FRAMEBUFFER, OpenGL.GL_COLOR_ATTACHMENT0, OpenGL.GL_TEXTURE_2D, texture, 0);
				OpenGL.CheckGLError();

				var canReadBGRA = OpenGL.Features.HasFlag(OpenGL.GLFeatures.ESReadFormatBGRA);

				unsafe
				{
					fixed (byte* ptr = &data[0])
					{
						var intPtr = new IntPtr(ptr);

						var format = canReadBGRA ? OpenGL.GL_BGRA : OpenGL.GL_RGBA;
						OpenGL.glReadPixels(0, 0, Size.Width, Size.Height, format, OpenGL.GL_UNSIGNED_BYTE, intPtr);
						OpenGL.CheckGLError();
					}
				}

				// Convert RGBA to BGRA
				if (!canReadBGRA)
				{
					for (var i = 0; i < 4 * Size.Width * Size.Height; i += 4)
					{
						(data[i + 2], data[i]) = (data[i], data[i + 2]);
					}
				}

				OpenGL.glBindFramebuffer(OpenGL.GL_FRAMEBUFFER, (uint)lastFramebuffer);
				OpenGL.glDeleteFramebuffers(1, ref framebuffer);
				OpenGL.CheckGLError();
			}
			else
			{
				OpenGL.glBindTexture(OpenGL.GL_TEXTURE_2D, texture);
				unsafe
				{
					fixed (byte* ptr = &data[0])
					{
						var intPtr = new IntPtr(ptr);
						OpenGL.glGetTexImage(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_BGRA,
							OpenGL.GL_UNSIGNED_BYTE, intPtr);
					}
				}

				OpenGL.CheckGLError();
			}

			return data;
		}

		public void SetEmpty(int width, int height)
		{
			VerifyThreadAffinity();
			if (!Exts.IsPowerOf2(width) || !Exts.IsPowerOf2(height))
				throw new InvalidDataException($"Non-power-of-two array {width}x{height}");

			Size = new Size(width, height);

			// Render targets use RGBA on GLES so that they can be copied with glCopyTexImage2D:
			// GLES drivers reject BGRA as the copy format (Mali) or BGRA to RGBA copies (Adreno)
			if (OpenGL.Profile == GLProfile.Embedded)
			{
				PrepareTexture();
				OpenGL.glTexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, width, height,
					0, OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, IntPtr.Zero);
				OpenGL.CheckGLError();
				return;
			}

			SetData(IntPtr.Zero, width, height);
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			OpenGL.glDeleteTextures(1, ref texture);
		}
	}
}
