// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;

namespace SomeCardGame.Protocol
{
    // Bridges YGOPro2's Package shape to the SCGS state store. Ocgcore keeps the
    // message id in Package.Fuction and the remaining bytes in Data.reader.
    public sealed class ScgsYgoProPackageAdapter
    {
        private readonly ScgsStateStore stateStore;

        public ScgsYgoProPackageAdapter(ScgsStateStore stateStore)
        {
            if (stateStore == null)
            {
                throw new ArgumentNullException("stateStore");
            }
            this.stateStore = stateStore;
        }

        public static bool IsScgsMessage(int function)
        {
            return function >= (int)ScgsGameMessage.GameMode
                && function <= (int)ScgsGameMessage.MatchStatistics;
        }

        public bool TryApply(int function, BinaryReader payloadReader, out string error)
        {
            error = null;
            if (!IsScgsMessage(function))
            {
                error = "Not an SCGS package function: " + function;
                return false;
            }
            if (payloadReader == null)
            {
                error = "SCGS package has no payload reader";
                return false;
            }

            Stream stream = payloadReader.BaseStream;
            long remaining = stream.Length - stream.Position;
            if (remaining < 0 || remaining > int.MaxValue)
            {
                error = "SCGS package payload length is invalid";
                return false;
            }

            byte[] payload = payloadReader.ReadBytes((int)remaining);
            if (payload.Length != (int)remaining)
            {
                error = "SCGS package payload is truncated";
                return false;
            }

            return stateStore.TryApply((ScgsGameMessage)(byte)function, payload, out error);
        }
    }
}
