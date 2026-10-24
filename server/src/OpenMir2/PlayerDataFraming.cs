using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenMir2
{
    // Used only by the GameSrv <-> DBSrv character-data channel. Legacy small
    // frames and every external client/gateway protocol retain their wire shape.
    public static class PlayerDataFraming
    {
        public const uint FragmentCode = Grobal2.PacketCode ^ 0x01000000;
        public const int MaxPayloadLength = 8 * 1024 * 1024;
        public const int FragmentPayloadLength = 60000;
        public const int FragmentHeaderLength = 16;
        private static long _nextTransfer;

        public static byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxPayloadLength) throw new InvalidDataException("Player data exceeds the transport limit");
            if (payload.Length <= ushort.MaxValue)
            {
                byte[] frame = new byte[6 + payload.Length];
                WriteHeader(frame, Grobal2.PacketCode, payload.Length);
                payload.CopyTo(frame, 6);
                return frame;
            }
            int fragments = (payload.Length + FragmentPayloadLength - 1) / FragmentPayloadLength;
            byte[] result = new byte[checked(payload.Length + fragments * (6 + FragmentHeaderLength))];
            long id = Interlocked.Increment(ref _nextTransfer);
            int output = 0;
            for (int offset = 0; offset < payload.Length; offset += FragmentPayloadLength)
            {
                int count = Math.Min(FragmentPayloadLength, payload.Length - offset);
                Span<byte> frame = result.AsSpan(output, 6 + FragmentHeaderLength + count);
                WriteHeader(frame, FragmentCode, FragmentHeaderLength + count);
                BinaryPrimitives.WriteInt64LittleEndian(frame.Slice(6, 8), id);
                BinaryPrimitives.WriteInt32LittleEndian(frame.Slice(14, 4), payload.Length);
                BinaryPrimitives.WriteInt32LittleEndian(frame.Slice(18, 4), offset);
                payload.AsSpan(offset, count).CopyTo(frame.Slice(22));
                output += frame.Length;
            }
            return result;
        }

        private static void WriteHeader(Span<byte> header, uint code, int length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header, code);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4), checked((ushort)length));
        }
    }

    // One instance per TCP connection. No global mutable frame/request objects.
    public sealed class PlayerDataFrameReader
    {
        private readonly byte[] _header = new byte[6];
        private int _headerCount, _bodyCount;
        private uint _code;
        private byte[] _body;
        private byte[] _transfer;
        private long _transferId, _deadline;
        private int _received;

        public void Reset()
        {
            _headerCount = _bodyCount = _received = 0;
            _body = _transfer = null;
            _transferId = _deadline = 0;
        }

        public List<byte[]> Feed(ReadOnlySpan<byte> input)
        {
            var messages = new List<byte[]>();
            try
            {
                while (!input.IsEmpty)
                {
                    if (_transfer != null && Environment.TickCount64 > _deadline)
                        throw new InvalidDataException("Player data transfer expired");
                    if (_body == null)
                    {
                        int count = Math.Min(6 - _headerCount, input.Length);
                        input.Slice(0, count).CopyTo(_header.AsSpan(_headerCount));
                        _headerCount += count;
                        input = input.Slice(count);
                        if (_headerCount < 6) break;
                        _code = BinaryPrimitives.ReadUInt32LittleEndian(_header);
                        if (_code != Grobal2.PacketCode && _code != PlayerDataFraming.FragmentCode)
                            throw new InvalidDataException("Invalid player data frame marker");
                        _body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(4))];
                        _bodyCount = 0;
                    }
                    int bodyCount = Math.Min(_body.Length - _bodyCount, input.Length);
                    input.Slice(0, bodyCount).CopyTo(_body.AsSpan(_bodyCount));
                    _bodyCount += bodyCount;
                    input = input.Slice(bodyCount);
                    if (_bodyCount < _body.Length) break;
                    byte[] body = _body;
                    _body = null;
                    _headerCount = 0;
                    byte[] complete = Accept(_code, body);
                    if (complete != null) messages.Add(complete);
                }
                return messages;
            }
            catch { Reset(); throw; }
        }

        public byte[] ReadFrame(uint code, byte[] body)
        {
            try
            {
                if (_transfer != null && Environment.TickCount64 > _deadline)
                    throw new InvalidDataException("Player data transfer expired");
                if (body == null || body.Length > ushort.MaxValue ||
                    (code != Grobal2.PacketCode && code != PlayerDataFraming.FragmentCode))
                    throw new InvalidDataException("Invalid player data frame");
                return Accept(code, body);
            }
            catch { Reset(); throw; }
        }

        private byte[] Accept(uint code, byte[] body)
        {
            if (code == Grobal2.PacketCode)
            {
                if (_transfer != null) throw new InvalidDataException("Player data transfer interrupted");
                return body;
            }
            if (body.Length <= PlayerDataFraming.FragmentHeaderLength)
                throw new InvalidDataException("Invalid player data fragment size");
            long id = BinaryPrimitives.ReadInt64LittleEndian(body);
            int total = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8));
            int offset = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12));
            int count = body.Length - PlayerDataFraming.FragmentHeaderLength;
            if (total <= ushort.MaxValue || total > PlayerDataFraming.MaxPayloadLength || offset < 0 || count > total - offset)
                throw new InvalidDataException("Invalid player data fragment bounds");
            if (_transfer == null)
            {
                if (offset != 0) throw new InvalidDataException("Missing first player data fragment");
                _transfer = new byte[total];
                _transferId = id;
                _received = 0;
                _deadline = Environment.TickCount64 + 30000;
            }
            if (_transferId != id || _transfer.Length != total || offset != _received)
                throw new InvalidDataException("Player data fragment sequence mismatch");
            body.AsSpan(PlayerDataFraming.FragmentHeaderLength).CopyTo(_transfer.AsSpan(offset));
            _received += count;
            if (_received != _transfer.Length) return null;
            byte[] completed = _transfer;
            _transfer = null;
            _received = 0;
            return completed;
        }
    }
}
