﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using HidLibrary;

namespace ChronUSBData
{
    /// <summary>
    /// Manages one Chron device.
    /// </summary>
    public class ChronManager : IDisposable
    {
        private const int VendorId = 0x0001;
        private const int ProductId = 0x0001;
        private HidDevice device;
        private bool attached = false;
        private bool connectedToDriver = false;
        private bool debugPrintRawMessages = true;
        private bool disposed = false;
		private int cron_entries_received = 0;

        /// <summary>
        /// Occurs when a Chron device is attached.
        /// </summary>
        public event EventHandler DeviceAttached;

        /// <summary>
        /// Occurs when a Chron device is removed.
        /// </summary>
        public event EventHandler DeviceRemoved;

		public event EventHandler<ChronEventArgs> EntryReceived;

        /// <summary>
        /// Attempts to connect to a Chron device.
        /// 
        /// After a successful connection, a DeviceAttached event will normally be sent.
        /// </summary>
        /// <returns>True if a Chron device is connected, False otherwise.</returns>
        public bool OpenDevice()
        {
            device = HidDevices.Enumerate(VendorId, ProductId).FirstOrDefault();

            if (device != null)
            {
                connectedToDriver = true;
                device.OpenDevice();

                device.Inserted += DeviceAttachedHandler;
                device.Removed += DeviceRemovedHandler;

                device.MonitorDeviceEvents = true;
				device.ReadReport(OnReport);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Closes the connection to the device.
        /// 
        /// FIXME: Verify that this also shuts down any thread waiting for device data. 2012-06-07 thammer.
        /// </summary>
        public void CloseDevice()
        {
            device.CloseDevice();
            connectedToDriver = false;
        }

        /// <summary>
        /// Closes the connection to the device.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Sends a message to the Chron device to enable pulsing of the LED.
        /// The (constant) LED brightness will be ignored. The pulse speed is set
        /// by the SetLedPulseBrightness() method.
        /// </summary>
        /// <param name="enable">True to enable pulsing, False otherwise.</param>
        public void SetLedPulseEnabled(bool enable)
        {
            if (connectedToDriver)
            {
                byte [] data = new byte[64];
                data[0] = 0x00;
                data[1] = 0x41;
                data[2] = 0x01;
                data[3] = 0x03; // command
                data[4] = 0x00;
                data[5] = enable ? (byte)0x01 : (byte)0x00;
                data[6] = 0x00;
                data[7] = 0x00;
                data[8] = 0x00;
                HidReport report = new HidReport(9, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
                device.WriteFeatureData(data);
            }
        }

        /// <summary>
        /// Sends a message to the Chron device to enable pulsing of the LED
        /// while the computer is sleeping.
        /// </summary>
        /// <param name="enable">True to enable pulsing during sleep, False otherwse.</param>
        public void SetLedPulseDuringSleepEnabled(bool enable)
        {
            if (connectedToDriver)
            {
                byte[] data = new byte[9];
                data[0] = 0x00;
                data[1] = 0x41;
                data[2] = 0x01; 
                data[3] = 0x02;
                data[4] = 0x00; // command
                data[5] = enable ? (byte)0x01 : (byte)0x00;
                data[6] = 0x00;
                data[7] = 0x00;
                data[8] = 0x00;
                HidReport report = new HidReport(9, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
                device.WriteFeatureData(data);
            }
        }

        /// <summary>
        /// Sets the Chron's LED pulse speed. The range is  [-255, 255], and the
        /// useful range seems to be approximately [-32, 64]. A value of 0 means default 
        /// pulse speed. A negative value is slower than the default, a positive value 
        /// is faster.
        /// </summary>
        /// <param name="speed"></param>
        public void SetLedPulseSpeed(int speed)
        {
            if (connectedToDriver)
            {
                byte[] data = new byte[9];
                data[0] = 0x00;
                data[1] = 0x41;
                data[2] = 0x01;
                data[3] = 0x04; // command
                data[4] = 0x00; // Table 0
                if (speed < 0)
                {
                    data[5] = 0;
                    data[6] = (byte)(-speed);
                } 
                else if (speed == 0)
                {
                    data[5] = 1;
                    data[6] = 0;
                }
                else // speed > 0
                {
                    data[5] = 2;
                    data[6] = (byte)(speed);
                }
                data[7] = 0x00;
                data[8] = 0x00;
                HidReport report = new HidReport(9, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
                device.WriteFeatureData(data);
            }
        }

        /// <summary>
        /// Sets the Chron's LED brightness. 
        /// </summary>
        /// <param name="brightness">The brightness of the LED. The valid range is [0, 255],
        /// with 0 being completely off and 255 being full brightness.
        /// </param>
        public void SetLedBrightness(int brightness)
        {
            if (connectedToDriver)
            {
                byte[] data = new byte[2];
                data[0] = 0;
                data[1] = (byte)brightness;
                HidReport report = new HidReport(2, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
                device.WriteReport(report);
            }
        }

        private void DeviceAttachedHandler()
        {
            attached = true;

            if (DeviceAttached != null)
                DeviceAttached(this, EventArgs.Empty);

			device.ReadReport(OnReport);
        }

        private void DeviceRemovedHandler()
        {
            attached = false;

            if (DeviceRemoved != null)
                DeviceRemoved(this, EventArgs.Empty);
        }


        /// <summary>
        /// Closes any connected devices.
        /// </summary>
        /// <param name="disposing"></param>
        private void Dispose(bool disposing)
        {
            if(!this.disposed)
            {
                if(disposing)
                {
                    CloseDevice();
                }

                disposed = true;
            }
        }

        /// <summary>
        /// Destroys instance and frees device resources (if not freed already)
        /// </summary>
		~ChronManager()
        {
            Dispose(false);
        }


		public void GetCronTime()
		{
			if (connectedToDriver)
			{
				byte[] data = new byte[2];
				data[0] = (byte)0x00;
				data[1] = (byte)0x00;
				HidReport report = new HidReport(2, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
				device.WriteReport(report);
			}
		}

		public void GetCronData()
		{
			if (connectedToDriver)
			{
				byte[] data = new byte[2];
				data[0] = 0x00;
				data[1] = 0x01;
				HidReport report = new HidReport(2, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
				device.WriteReport(report);
				ChronState state = new ChronState();
				if (report.Data.Length > 2)
				{
					if (report.Data[0] == 0 && report.Data[1] == 2)
					{
						state.Diagnostics = "Received Chron Entry " + cron_entries_received + ": ";
						state.Read_MoreToParse = true;
						cron_entries_received++;
						//this.GetCronDataContinuation();
					}

					if (report.Data[0] == 0 && report.Data[1] == 3)
					{
						state.Diagnostics = "Chron Entry Comms Ended: ";
						cron_entries_received = 0;
					}

					if (debugPrintRawMessages)
					{
						for (int i = 0; i < report.Data.Length; i++)
						{
							state.Diagnostics += report.Data[i] + ", ";
						}

						System.Diagnostics.Debug.WriteLine(state.Diagnostics);
					}

					this.OnEntryReceived(state);
				}
			}
		}

		public void GetCronDataContinuation()
		{
			if (connectedToDriver)
			{
				byte[] data = new byte[2];
				data[0] = (byte)0x02;
				data[1] = (byte)0x02;
				HidReport report = new HidReport(2, new HidDeviceData(data, HidDeviceData.ReadStatus.Success));
				device.WriteReport(report);
			}
		}

		private void OnReport(HidReport report)
		{
			ChronState state = new ChronState();

			if (attached == false) { return; }

			if (report.Data.Length > 2)
			{
				if (report.Data[0] == 0 && report.Data[1] == 2)
				{
					state.Diagnostics = "Received Chron Entry " + cron_entries_received + ": ";
					state.Read_MoreToParse = true;
					cron_entries_received++;
					//this.GetCronDataContinuation();
				} 
				
				if (report.Data[0] == 0 && report.Data[1] == 3)
				{
					state.Diagnostics = "Chron Entry Comms Ended: ";
					cron_entries_received = 0;
				}

				if (debugPrintRawMessages)
				{
					for (int i = 0; i < report.Data.Length; i++)
					{
						state.Diagnostics += report.Data[i] + ", ";
					}

					System.Diagnostics.Debug.WriteLine(state.Diagnostics);
				}

				this.OnEntryReceived(state);
			}

			device.ReadReport(OnReport);
		}

		
		private void OnEntryReceived(ChronState state)
		{
			var handle = this.EntryReceived;
			if (handle != null)
			{
				handle(this, new ChronEventArgs(state));
			}
		}
    }
}
