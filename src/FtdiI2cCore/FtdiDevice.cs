using System;
using FtdiCore._3rdParty;

namespace FtdiCore
{
    public class FtdiDevice
    {
        private readonly FTDI.FT_DEVICE_INFO_NODE _reference;

        internal FtdiDevice(FTDI.FT_DEVICE_INFO_NODE device)
        {
            _reference = device;
        }

        public string Description => _reference.Description;

        public string SerialNumber => _reference.SerialNumber;

        /// <summary>
        /// Vendor ID and Product Id of Device
        /// </summary>
        public UInt32 Id => _reference.ID;

        /// <summary>
        /// Physical Location ID
        /// </summary>
        public UInt32 LocationId => _reference.LocId;
    }
}
