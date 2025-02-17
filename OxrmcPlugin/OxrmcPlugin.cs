using OxrmcPlugin.Properties;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;
using YawGLAPI;
using System.Linq;
using System.Threading.Tasks;

namespace YawVR_Game_Engine.Plugin
{
    [Export(typeof(Game))]
    [ExportMetadata("Name", "OXRMC COR Estimator")]
    [ExportMetadata("Version", "1.0")]
	
    class OxrmcPlugin : Game
    {
		[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
		[Serializable]
		private struct Telemetry
		{
			public double Sway;
			public double Surge;
			public double Heave;
			public double Yaw;
			public double Roll;
			public double Pitch;
			public bool Active;
			public bool Connected;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
			public byte[] Reserved;

			public byte[] GetBytes()
			{
				int size = Marshal.SizeOf(this);
				IntPtr ptr = Marshal.AllocHGlobal(size);
				byte[] buffer = new byte[size];

				Marshal.StructureToPtr(this, ptr, true);
				Marshal.Copy(ptr, buffer, 0, size);
				Marshal.FreeHGlobal(ptr);

				return buffer;
			}
		};

		private IProfileManager _controller;
        private IMainFormDispatcher _dispatcher;

        private Thread _readThread;
        private bool _running = false;
        private MemoryMappedFile _sharedMemory;
        private MemoryMappedViewAccessor _viewAccessor;

        public string PROCESS_NAME => "OxrmcConfigurator";
        public int STEAM_ID => 0;
        public string AUTHOR => "BuzzteeBear";
        public bool PATCH_AVAILABLE => false;

        public Stream Logo => GetStream("logo.png");
        public Stream SmallLogo => GetStream("recent.png");
        public Stream Background => GetStream("wide.png");
        public string Description => String.Empty;

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
            bool mmfFound = false;
            _running = true;
            Task.Run(async delegate
            {
                do
                {
                    try
                    {
                        _sharedMemory = MemoryMappedFile.OpenExisting("Local\\OXRMC_Telemetry");
                        _viewAccessor = _sharedMemory.CreateViewAccessor(0, Marshal.SizeOf(typeof(Telemetry)));

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

        private void SetConnectedFlag( bool flag)
        {
	        try
	        {
		        var data = ReadTelemetry();
		        using var stream = _sharedMemory.CreateViewStream();
		        using var writer = new BinaryWriter(stream);
		        data.Connected = flag;
		        var bytes = data.GetBytes();
		        writer?.Write(bytes, 0, bytes.Length);
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
	        var data = (Telemetry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Telemetry));
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
                    _controller.SetInput(8, 0f); // Reserved

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
                Interaction.MsgBox($"Error reading shared memory: {ex.Message}", MsgBoxStyle.Critical, "Error");
            }
        }

        public string[] GetInputData()
        {
	        return typeof(Telemetry).GetFields().Select(field => field.Name).ToArray();
        }

        public LedEffect DefaultLED()
        {
			return _dispatcher.JsonToLED(Resources.defProfile);
		}

        public List<Profile_Component> DefaultProfile()
        {
            return _dispatcher.JsonToComponents(Resources.defProfile);
        }

        public Dictionary<string, ParameterInfo[]> GetFeatures()
        {
            return null;
        }

        Stream GetStream(string resourceName)
        {
            var assembly = GetType().Assembly;
            string fullResourceName = $"{assembly.GetName().Name}.Resources.{resourceName}";
            return assembly.GetManifestResourceStream(fullResourceName);
        }
    }
}