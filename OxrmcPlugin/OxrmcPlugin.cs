using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using YawGLAPI;
using SharedLib;

namespace YawVR_Game_Engine.Plugin
{
	[Export(typeof(Game))]
	[ExportMetadata("Name", "OXRMC COR Estimator")]
	[ExportMetadata("Version", "1.0")]

	class OxrmcPlugin : Game
	{
		[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
		[Serializable]
		private struct Telemetry
		{
			// output
			[FieldOffset(0)] public double Sway;
			[FieldOffset(8)] public double Surge;
			[FieldOffset(16)] public double Heave;
			[FieldOffset(24)] public double Yaw;
			[FieldOffset(32)] public double Roll;
			[FieldOffset(40)] public double Pitch;
			[FieldOffset(48)] public bool Active;

			// for future extension
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
			[FieldOffset(56)] public byte[] Unused;

			// input
			[FieldOffset(64)] public double OffsetSway;
			[FieldOffset(72)] public double OffsetSurge;
			[FieldOffset(80)] public double OffsetHeave;
			[FieldOffset(88)] public double OffsetYaw;
			[FieldOffset(96)] public double OffsetRoll;
			[FieldOffset(104)] public double OffsetPitch;
			[FieldOffset(112)] public bool Connected;

			// for future extension
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 392)]
			[FieldOffset(120)] public byte[] Reserved;
		}

		private IProfileManager _controller;
		private IMainFormDispatcher _dispatcher;

		private Thread _readThread;
		private bool _running;
		private MemoryMappedFile _sharedMemory;

		public string PROCESS_NAME => "CorEstimator";
		public int STEAM_ID => 0;
		public string AUTHOR => "BuzzteeBear";
		public bool PATCH_AVAILABLE => false;

		public Stream Logo => ResourceHelper.GetStream("logo.png");
		public Stream SmallLogo => ResourceHelper.GetStream("recent.png");
		public Stream Background => ResourceHelper.GetStream("wide.png");
		public string Description => ResourceHelper.GetString("description.html");
		private string ProfileJson => ResourceHelper.GetString("default.yawglprofile");

		public void PatchGame()
		{
		}

		public void Exit()
		{
			_running = false;
			_readThread?.Join();
			_sharedMemory?.Dispose();
		}

		public void SetReferences(IProfileManager controller, IMainFormDispatcher dispatcher)
		{
			this._controller = controller;
			this._dispatcher = dispatcher;
		}

		public void Init()
		{
			var mmfFound = false;
			_running = true;
			Task.Run(async delegate
			{
				do
				{
					try
					{
						_sharedMemory = MemoryMappedFile.OpenExisting("Local\\OXRMC_Telemetry");

						SetConnectedFlag(true);

						mmfFound = true;
						_readThread = new Thread(ReadFunction);
						_readThread.Start();
					}
					catch (FileNotFoundException)
					{
						//Keep trying, unless plugin is stopped
						await Task.Delay(1000);
					}
				} while (!mmfFound && _running);
			});
		}

		private void SetConnectedFlag(bool flag)
		{
			try
			{
				using var stream = _sharedMemory.CreateViewStream();
				using var writer = new BinaryWriter(stream);
				writer.Seek(112, SeekOrigin.Begin);
				writer.Write(flag);
			}
			catch (FileNotFoundException)
			{
				// no point in writing to non-existent mmf
			}
		}

		private Telemetry ReadTelemetry()
		{
			using var stream = _sharedMemory.CreateViewStream();
			using var reader = new BinaryReader(stream);

			var size = Marshal.SizeOf(typeof(Telemetry));
			var bytes = reader.ReadBytes(size);
			var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			var data = (Telemetry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Telemetry))!;
			handle.Free();
			return data;
		}

		private void ReadFunction()
		{
			try
			{
				while (_running)
				{
					// Read the Physics structure
					var telemetry = ReadTelemetry();

					// Map data to controller inputs
					_controller.SetInput(0, (float)-telemetry.Yaw);
					_controller.SetInput(1, (float)-telemetry.Roll);
					_controller.SetInput(2, (float)-telemetry.Pitch);
					_controller.SetInput(3, (float)telemetry.Sway);
					_controller.SetInput(4, (float)telemetry.Surge);
					_controller.SetInput(5, (float)telemetry.Heave);
					_controller.SetInput(6, telemetry.Active ? 1f : 0f);
					_controller.SetInput(7, telemetry.Connected ? 1f : 0f);

					Thread.Sleep(10); // Poll at 100 Hz
				}

				SetConnectedFlag(false);
			}
			catch (ThreadAbortException)
			{
				// Thread was aborted
			}
			catch (Exception ex)
			{
				System.Windows.MessageBox.Show($"Failure reading from shared memory:\n{ex}",
					"Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		public string[] GetInputData()
		{
			return ["yaw", "roll", "pitch", "sway", "surge", "heave", "active", "connected"];
		}
		public LedEffect DefaultLED()
		{
			return _dispatcher.JsonToLED(ProfileJson);
		}

		public List<Profile_Component> DefaultProfile()
		{
			return _dispatcher.JsonToComponents(ProfileJson);
		}

		public Dictionary<string, ParameterInfo[]> GetFeatures()
		{
			return null;
		}

		public Type GetConfigBody()
		{
			return null;
		}
	}
}