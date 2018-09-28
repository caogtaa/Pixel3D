﻿// Copyright © Conatus Creative, Inc. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE.md in the project root for license terms.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Pixel3D.Audio
{
	public static class ReadAudioPackage
	{
		private static void ThrowError()
		{
			throw new Exception("Audio Package Corrupt");
		}

		public static Result ReadHeader(string path, byte[] header)
		{
			Result result;

#if !WINDOWS
			path = path.Replace('\\', '/');
#endif
			result.audioPackageBytes = File.ReadAllBytes(path);
			var ms = new MemoryStream(result.audioPackageBytes);

			//
			// Magic Number:
			if (result.audioPackageBytes.Length < header.Length)
				ThrowError();
			for (var i = 0; i < header.Length; i++)
				if (ms.ReadByte() != header[i])
					ThrowError();

			//
			// Audio File Table:
			var integerReadBuffer = new byte[4];
			if (ms.Read(integerReadBuffer, 0, 4) != 4)
				ThrowError();
			var indexLength = BitConverter.ToInt32(integerReadBuffer, 0);
			result.vorbisOffset = indexLength + (int) ms.Position;

			using (var br = new BinaryReader(new GZipStream(ms, CompressionMode.Decompress, true)))
			{
				var count = br.ReadInt32();
				result.offsets = new int[count + 1]; // <- For simplicity, offsets[0] = 0 (start of first sound)
				result.sounds = new SafeSoundEffect[count];
				result.lookup = new OrderedDictionary<string, SafeSoundEffect>(count);
				for (var i = 0; i < count; i++)
				{
					result.lookup.Add(br.ReadString(), result.sounds[i] = new SafeSoundEffect());
					result.offsets[i + 1] = br.ReadInt32();
				}
			}

			return result;
		}

		public struct Result
		{
			public IDictionary<string, SafeSoundEffect> lookup;

			public byte[] audioPackageBytes;
			public int vorbisOffset;
			public int[] offsets;
			public SafeSoundEffect[] sounds;
		}
	}
}