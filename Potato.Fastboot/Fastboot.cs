using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace Potato.Fastboot
{
    /// <summary>
    /// The main Fastboot class.
    /// Contains methods to communicate with fastboot device.
    /// </summary>
    /// <remarks>
    /// Available methods: wait, connect, disconnect, get serial number, execute command, upload data and get devices.
    /// </remarks>
    public class Fastboot
    {
        private const int USB_VID = 0x18D1;
        private const int USB_PID = 0xD00D;
        private const int HEADER_SIZE = 4;
        private const int BLOCK_SIZE = 512 * 1024; // 512 KB

        public int Timeout { get; set; } = 3000;

        private readonly UsbContext context;
        private IUsbDevice device;

        public enum Status
        {
            Fail,
            Okay,
            Data,
            Info,
            Unknown
        }

        public class Response
        {
            public Status Status { get; set; }
            public string Payload { get; set; }
            public byte[] RawData { get; set; }

            public Response(Status status, string payload)
            {
                Status = status;
                Payload = payload;
            }
        }

        public Fastboot(LogLevel logLevel = LogLevel.None)
        {
            context = new UsbContext();
            context.SetDebugLevel(logLevel);
        }

        private Status GetStatusFromString(string header)
        {
            switch (header)
            {
                case "INFO":
                    return Status.Info;
                case "OKAY":
                    return Status.Okay;
                case "DATA":
                    return Status.Data;
                case "FAIL":
                    return Status.Fail;
                default:
                    return Status.Unknown;
            }
        }


        /// <summary>
        /// Wait of any device.
        /// </summary>
        public void Wait()
        {
            var counter = 0;
            UsbDeviceCollection devices;

            while (true)
            {
                if (counter == 50)
                {
                    throw new Exception("Timeout error.");
                }

                devices = context.List();

                if (devices.Any(x => x.VendorId == USB_VID && x.ProductId == USB_PID))
                {
                    return;
                }

                Thread.Sleep(500);
                counter++;
            }
        }

        /// <summary>
        /// Connect to first fastboot device.
        /// </summary>
        public void Connect()
        {
            var devices = context.List();
            device = devices.FirstOrDefault(x => x.VendorId == USB_VID && x.ProductId == USB_PID);

            if (device == null)
            {
                throw new Exception("No devices available.");
            }

            device.Open();
            device.ClaimInterface(device.Configs[0].Interfaces[0].Number);
        }

        /// <summary>
        /// Disconnect from device.
        /// </summary>
        public void Disconnect()
        {
            device.Close();
        }

        /// <summary>
        /// Get device serial number.
        /// </summary>
        /// <returns>Serial number</returns>
        public string GetSerialNumber()
        {
            return device.Info.SerialNumber;
        }

        /// <summary>
        /// Send command to device and read response.
        /// </summary>
        /// <param name="command">Command as bytes array</param>
        /// <returns>Parsed response from device</returns>
        public Response Command(byte[] command)
        {
            var writeEndpoint = device.OpenEndpointWriter(WriteEndpointID.Ep01);
            var readEndpoint = device.OpenEndpointReader(ReadEndpointID.Ep01);

            writeEndpoint.Write(command, Timeout, out int wrAct);

            if (wrAct != command.Length)
            {
                throw new Exception($"Failed to write command! Transfered: {wrAct} of {command.Length} bytes");
            }

            Status status;
            var response = new StringBuilder();
            var buffer = new byte[64];
            string strBuffer;

            while (true)
            {
                readEndpoint.Read(buffer, Timeout, out int rdAct);

                strBuffer = Encoding.ASCII.GetString(buffer);

                if (strBuffer.Length < HEADER_SIZE)
                {
                    status = Status.Unknown;
                }
                else
                {
                    var header = new string(strBuffer
                        .Take(HEADER_SIZE)
                        .ToArray());

                    status = GetStatusFromString(header);
                }

                response.Append(strBuffer
                    .Skip(HEADER_SIZE)
                    .Take(rdAct - HEADER_SIZE)
                    .ToArray());

                response.Append("\n");

                if (status != Status.Info)
                {
                    break;
                }
            }

            var str = response
                .ToString()
                .Replace("\r", string.Empty)
                .Replace("\0", string.Empty);

            return new Response(status, str)
            {
                RawData = Encoding.ASCII.GetBytes(strBuffer)
            };
        }

        /// <summary>
        /// Send data command to device.
        /// </summary>
        /// <param name="size">Data size (bytes)</param>
        private void SendDataCommand(long size)
        {
            if (Command($"download:{size:X8}").Status != Status.Data)
            {
                throw new Exception($"Invalid response from device! (data size: {size})");
            }
        }

        /// <summary>
        /// Transfer block to device from stream to USB write endpoint with fixed size
        /// </summary>
        /// <param name="stream">Input stream</param>
        /// <param name="writeEndpoint">USB write endpoint</param>
        /// <param name="buffer">Block of data</param>
        /// <param name="size">Data size (bytes)</param>
        private void TransferBlock(FileStream stream, UsbEndpointWriter writeEndpoint, byte[] buffer, int size)
        {
            stream.Read(buffer, 0, size);
            writeEndpoint.Write(buffer, Timeout, out int act);

            if (act != size)
            {
                throw new Exception($"Failed to transfer block (sent {act} of {size})");
            }
        }

        /// <summary>
        /// Upload data from <c>FileStream</c> to device's buffer
        /// </summary>
        /// <param name="stream">Input FileStream</param>
        public void UploadData(FileStream stream)
        {
            var writeEndpoint = device.OpenEndpointWriter(WriteEndpointID.Ep01);
            var readEndpoint = device.OpenEndpointReader(ReadEndpointID.Ep01);

            var length = stream.Length;
            var buffer = new byte[BLOCK_SIZE];

            SendDataCommand(length);

            while (length >= BLOCK_SIZE)
            {
                TransferBlock(stream, writeEndpoint, buffer, BLOCK_SIZE);
                length -= BLOCK_SIZE;
            }

            if (length > 0)
            {
                buffer = new byte[length];
                TransferBlock(stream, writeEndpoint, buffer, (int)length);
            }

            var resBuffer = new byte[64];

            readEndpoint.Read(resBuffer, Timeout, out _);

            var strBuffer = Encoding.ASCII.GetString(resBuffer);

            if (strBuffer.Length < HEADER_SIZE)
            {
                throw new Exception($"Invalid response from device: {strBuffer}");
            }

            var header = new string(strBuffer
                .Take(HEADER_SIZE)
                .ToArray());

            var status = GetStatusFromString(header);

            if (status != Status.Okay)
            {
                throw new Exception($"Invalid status: {strBuffer}");
            }
        }

        /// <summary>
        /// Upload data from file to device's buffer
        /// </summary>
        /// <param name="path">Path to file</param>
        public void UploadData(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open))
            {
                UploadData(stream);
            }
        }

        /// <summary>
        /// Get list of available devices
        /// </summary>
        /// <returns>Array of serial numbers</returns>
        public static string[] GetDevices()
        {
            var devices = new List<string>();

            using (var context = new UsbContext())
            {
                var allDevices = context.List();

                foreach (var usbRegistry in allDevices)
                {
                    if (usbRegistry.VendorId != USB_VID || usbRegistry.ProductId != USB_PID)
                    {
                        continue;
                    }

                    if (usbRegistry.TryOpen())
                    {
                        devices.Add(usbRegistry.Info.SerialNumber);
                    }

                    usbRegistry.Close();
                }
            }

            return devices.ToArray();
        }

        /// <summary>
        /// Send command to device and read response.
        /// </summary>
        /// <param name="command">Command</param>
        /// <returns>Parsed response from device</returns>
        public Response Command(string command)
        {
            return Command(Encoding.ASCII.GetBytes(command));
        }
    }
}
