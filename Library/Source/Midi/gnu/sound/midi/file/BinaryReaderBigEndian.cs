﻿using System;
using System.IO;
using System.Linq;

namespace gnu.sound.midi.file
{
	/// <summary>
	/// BigEndian BinaryReader.
	/// </summary>
	public class BinaryReaderBigEndian : BinaryReader {
		
		private byte[] a16 = new byte[2];
		private byte[] a32 = new byte[4];
		private byte[] a64 = new byte[8];
		
		public BinaryReaderBigEndian(Stream stream)  : base(stream) { }
		
		public override int ReadInt32()
		{
			a32 = base.ReadBytes(4);
			Array.Reverse(a32);
			return BitConverter.ToInt32(a32,0);
		}
		
		public override Int16 ReadInt16()
		{
			a16 = base.ReadBytes(2);
			Array.Reverse(a16);
			return BitConverter.ToInt16(a16, 0);
		}
		
		public override Int64 ReadInt64()
		{
			a64 = base.ReadBytes(8);
			Array.Reverse(a64);
			return BitConverter.ToInt64(a64, 0);
		}
		
		public override UInt32 ReadUInt32()
		{
			a32 = base.ReadBytes(4);
			Array.Reverse(a32);
			return BitConverter.ToUInt32(a32, 0);
		}
		
		public sbyte[] ReadSBytes(int length) {
			var bytes = base.ReadBytes(length);
			var sbytes = MidiHelper.ConvertBytes(bytes);
			return sbytes;
		}
	}
}
