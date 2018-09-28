﻿// Copyright © Conatus Creative, Inc. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE.md in the project root for license terms.
using System;
using Pixel3D.Audio;
using Pixel3D.PostProcessing;

namespace Pixel3D.UI
{
	/// <summary>
	/// Defines context methods that are safe to call from a networked game.
	/// This typically means read-only access, or the possible write operations do not touch the simulation.
	/// If the implementation cannot satisfy these requirements, then it should throw.
	/// </summary>
	public interface INetworkSafeContext
	{
		TimeSpan Uptime { get; } // todo remove me?
		IPostProcessProvider PostProcessProvider { get; } // todo remove me?
		bool TryGetAudioPlayer(out IAudioPlayer audioPlayer);
	}
}