// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace SomeCardGame.Protocol
{
    public static class ScgsProtocolReader
    {
        public const byte ProtocolVersion = 1;

        // A complete SCGS wire message contains the one-byte message id followed
        // by the payload. YGOPro2 stores that id in Package.Fuction and exposes
        // only the payload through Package.Data.reader, so both shapes are
        // intentionally supported.
        public const int PlayerStateMessageLength = 12;
        public const int PlayerStatePayloadLength = 11;
        public const int UnitStateMessageLength = 24;
        public const int UnitStatePayloadLength = 23;

        public static ScgsPlayerState DecodePlayerState(byte[] message)
        {
            RequireLength(message, PlayerStateMessageLength, "PlayerState message");
            int offset = 0;
            RequireMessageId(message, ref offset, ScgsGameMessage.PlayerState);
            return DecodePlayerStateBody(message, ref offset, "PlayerState message");
        }

        public static ScgsPlayerState DecodePlayerStatePayload(byte[] payload)
        {
            RequireLength(payload, PlayerStatePayloadLength, "PlayerState payload");
            int offset = 0;
            return DecodePlayerStateBody(payload, ref offset, "PlayerState payload");
        }

        public static ScgsUnitState DecodeUnitState(byte[] message)
        {
            RequireLength(message, UnitStateMessageLength, "UnitState message");
            int offset = 0;
            RequireMessageId(message, ref offset, ScgsGameMessage.UnitState);
            return DecodeUnitStateBody(message, ref offset, "UnitState message");
        }

        public static ScgsUnitState DecodeUnitStatePayload(byte[] payload)
        {
            RequireLength(payload, UnitStatePayloadLength, "UnitState payload");
            int offset = 0;
            return DecodeUnitStateBody(payload, ref offset, "UnitState payload");
        }

        private static ScgsPlayerState DecodePlayerStateBody(byte[] bytes, ref int offset, string name)
        {
            RequireVersion(bytes, ref offset);

            ScgsPlayerState state = new ScgsPlayerState();
            state.Player = ReadByte(bytes, ref offset);
            state.LeaderHealth = ReadInt16(bytes, ref offset);
            state.MaximumLeaderHealth = ReadInt16(bytes, ref offset);
            state.CurrentPP = ReadByte(bytes, ref offset);
            state.MaximumPP = ReadByte(bytes, ref offset);
            state.EvolutionPoints = ReadByte(bytes, ref offset);
            state.OwnTurnNumber = ReadByte(bytes, ref offset);
            state.Flags = (ScgsPlayerFlags)ReadByte(bytes, ref offset);
            RequireConsumed(bytes, offset, name);
            return state;
        }

        private static ScgsUnitState DecodeUnitStateBody(byte[] bytes, ref int offset, string name)
        {
            RequireVersion(bytes, ref offset);

            ScgsUnitState state = new ScgsUnitState();
            state.Controller = ReadByte(bytes, ref offset);
            state.Sequence = ReadByte(bytes, ref offset);
            state.InstanceId = ReadUInt64(bytes, ref offset);
            state.Attack = ReadInt16(bytes, ref offset);
            state.Health = ReadInt16(bytes, ref offset);
            state.MaximumHealth = ReadInt16(bytes, ref offset);
            state.Keywords = ReadUInt32(bytes, ref offset);
            state.InheritedImprint = ReadByte(bytes, ref offset);
            state.Flags = (ScgsUnitFlags)ReadByte(bytes, ref offset);
            RequireConsumed(bytes, offset, name);
            return state;
        }

        private static void RequireMessageId(byte[] bytes, ref int offset, ScgsGameMessage expected)
        {
            byte message = ReadByte(bytes, ref offset);
            if (message != (byte)expected)
            {
                throw new ArgumentException("Unexpected SCGS message id: " + message);
            }
        }

        private static void RequireVersion(byte[] bytes, ref int offset)
        {
            byte version = ReadByte(bytes, ref offset);
            if (version != ProtocolVersion)
            {
                throw new ArgumentException("Unsupported SCGS protocol version: " + version);
            }
        }

        private static void RequireLength(byte[] bytes, int expected, string name)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }
            if (bytes.Length != expected)
            {
                throw new ArgumentException(name + " has length " + bytes.Length + ", expected " + expected);
            }
        }

        private static void RequireConsumed(byte[] bytes, int offset, string name)
        {
            if (offset != bytes.Length)
            {
                throw new ArgumentException(name + " contains trailing bytes");
            }
        }

        private static byte ReadByte(byte[] bytes, ref int offset)
        {
            RequireAvailable(bytes, offset, 1);
            return bytes[offset++];
        }

        private static short ReadInt16(byte[] bytes, ref int offset)
        {
            ushort raw = ReadUInt16(bytes, ref offset);
            return unchecked((short)raw);
        }

        private static ushort ReadUInt16(byte[] bytes, ref int offset)
        {
            RequireAvailable(bytes, offset, 2);
            ushort value = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            offset += 2;
            return value;
        }

        private static uint ReadUInt32(byte[] bytes, ref int offset)
        {
            RequireAvailable(bytes, offset, 4);
            uint value = (uint)bytes[offset]
                       | ((uint)bytes[offset + 1] << 8)
                       | ((uint)bytes[offset + 2] << 16)
                       | ((uint)bytes[offset + 3] << 24);
            offset += 4;
            return value;
        }

        private static ulong ReadUInt64(byte[] bytes, ref int offset)
        {
            RequireAvailable(bytes, offset, 8);
            ulong value = 0;
            for (int index = 0; index < 8; ++index)
            {
                value |= ((ulong)bytes[offset + index]) << (index * 8);
            }
            offset += 8;
            return value;
        }

        private static void RequireAvailable(byte[] bytes, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentException("Truncated SCGS protocol message");
            }
        }
    }
}
