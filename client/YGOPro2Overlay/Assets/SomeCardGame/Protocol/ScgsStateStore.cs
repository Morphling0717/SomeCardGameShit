// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace SomeCardGame.Protocol
{
    public sealed class ScgsStateStore
    {
        private readonly ScgsPlayerState[] players = new ScgsPlayerState[2];
        private readonly Dictionary<ulong, ScgsUnitState> units = new Dictionary<ulong, ScgsUnitState>();

        public event Action<ScgsPlayerState> PlayerStateChanged;
        public event Action<ScgsUnitState> UnitStateChanged;

        public ScgsPlayerState GetPlayer(byte player)
        {
            if (player > 1)
            {
                throw new ArgumentOutOfRangeException("player");
            }
            return players[player];
        }

        public bool TryGetUnit(ulong instanceId, out ScgsUnitState state)
        {
            return units.TryGetValue(instanceId, out state);
        }

        // Useful for standalone golden-vector tests, where the first byte is the
        // SCGS message id.
        public bool TryApply(byte[] message, out string error)
        {
            error = null;
            if (message == null || message.Length < 2)
            {
                error = "SCGS message is missing or too short";
                return false;
            }

            ScgsGameMessage messageId = (ScgsGameMessage)message[0];
            try
            {
                switch (messageId)
                {
                    case ScgsGameMessage.PlayerState:
                        ApplyPlayer(ScgsProtocolReader.DecodePlayerState(message));
                        return true;
                    case ScgsGameMessage.UnitState:
                        ApplyUnit(ScgsProtocolReader.DecodeUnitState(message));
                        return true;
                    default:
                        error = "SCGS message is not implemented by the overlay: " + message[0];
                        return false;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        // This is the normal YGOPro2 path. Package.Fuction already contains the
        // message id; Package.Data.reader begins at the protocol-version byte.
        public bool TryApply(ScgsGameMessage messageId, byte[] payload, out string error)
        {
            error = null;
            if (payload == null || payload.Length < 1)
            {
                error = "SCGS payload is missing or too short";
                return false;
            }

            try
            {
                switch (messageId)
                {
                    case ScgsGameMessage.PlayerState:
                        ApplyPlayer(ScgsProtocolReader.DecodePlayerStatePayload(payload));
                        return true;
                    case ScgsGameMessage.UnitState:
                        ApplyUnit(ScgsProtocolReader.DecodeUnitStatePayload(payload));
                        return true;
                    default:
                        error = "SCGS payload is not implemented by the overlay: " + (byte)messageId;
                        return false;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void ApplyPlayer(ScgsPlayerState state)
        {
            if (state.Player > 1)
            {
                throw new ArgumentOutOfRangeException("state.Player");
            }
            players[state.Player] = state;
            Action<ScgsPlayerState> handler = PlayerStateChanged;
            if (handler != null)
            {
                handler(state);
            }
        }

        private void ApplyUnit(ScgsUnitState state)
        {
            if (state.Controller > 1 || state.Sequence > 4)
            {
                throw new ArgumentOutOfRangeException("unit position");
            }
            units[state.InstanceId] = state;
            Action<ScgsUnitState> handler = UnitStateChanged;
            if (handler != null)
            {
                handler(state);
            }
        }
    }
}
