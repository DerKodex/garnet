﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Garnet.common;
using Garnet.common.Parsing;

namespace Garnet.client
{
    public static unsafe class RespReadResponseUtils
    {
        /// <inheritdoc cref="RespReadUtils.TryReadSimpleString(out string, ref byte*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadSimpleString(out string result, ref byte* ptr, byte* end)
            => RespReadUtils.TryReadSimpleString(out result, ref ptr, end);

        /// <summary>
        /// Read simple string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadSimpleString(MemoryPool<byte> pool, out MemoryResult<byte> result, ref byte* ptr, byte* end)
        {
            result = default;
            if (ptr + 2 >= end)
                return false;

            // Simple strings need to start with a '+'
            if (*ptr != '+')
            {
                RespParsingException.ThrowUnexpectedToken(*ptr);
            }

            ptr++;

            return TryReadString(pool, out result, ref ptr, end);
        }

        /// <inheritdoc cref="RespReadUtils.TryReadIntegerAsString(out string, ref byte*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadIntegerAsString(out string result, ref byte* ptr, byte* end)
            => RespReadUtils.TryReadIntegerAsString(out result, ref ptr, end);

        /// <summary>
        /// Read integer as string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadIntegerAsString(MemoryPool<byte> pool, out MemoryResult<byte> result, ref byte* ptr, byte* end)
        {
            result = default;
            if (ptr + 2 >= end)
                return false;

            // Integer strings need to start with a ':'
            if (*ptr != ':')
            {
                RespParsingException.ThrowUnexpectedToken(*ptr);
            }

            ptr++;

            return TryReadString(pool, out result, ref ptr, end);
        }

        /// <summary>
        /// Try to read a RESP formatted bulk string
        /// NOTE: This is used with client implementation to parse responses that may include a null value (i.e. $-1\r\n)
        /// </summary>
        /// <param name="result"></param>
        /// <param name="ptr"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadStringWithLengthHeader(out string result, ref byte* ptr, byte* end)
        {
            result = null;

            byte* keyPtr = null;
            var length = 0;
            if (!TryReadPtrWithSignedLengthHeader(ref keyPtr, ref length, ref ptr, end))
                return false;

            if (length < 0)
                return true;

            result = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(keyPtr, length));
            return true;
        }

        /// <summary>
        /// Read string with length header
        /// </summary>
        /// <param name="pool">Memory pool to rent space for storing the result.</param>
        /// <param name="result">If parsing was successful, contains the extracted byte sequence.</param>
        /// <param name="ptr">The starting position in the RESP message. Will be advanced if parsing is successful.</param>
        /// <param name="end">The current end of the RESP message.</param>
        /// <returns>True if a RESP string was successfully read.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadStringWithLengthHeader(MemoryPool<byte> pool, out MemoryResult<byte> result, ref byte* ptr, byte* end)
        {
            result = default;

            byte* keyPtr = null;
            var length = 0;
            if (!TryReadPtrWithSignedLengthHeader(ref keyPtr, ref length, ref ptr, end))
                return false;

            if (length < 0)
                return true;

            result = MemoryResult<byte>.Create(pool, length);
            new ReadOnlySpan<byte>(keyPtr, length).CopyTo(result.Span);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool TryReadPtrWithSignedLengthHeader(ref byte* keyPtr, ref int length, ref byte* ptr, byte* end)
        {
            // Parse RESP string header
            if (!RespReadUtils.TryReadSignedLengthHeader(out length, ref ptr, end))
            {
                return false;
            }

            // Allow for null
            if (length < 0)
            {
                // NULL value ('$-1\r\n')
                keyPtr = null;
                return true;
            }

            keyPtr = ptr;

            // Parse content: ensure that input contains key + '\r\n'
            ptr += length + 2;
            if (ptr > end)
            {
                return false;
            }

            if (*(ushort*)(ptr - 2) != MemoryMarshal.Read<ushort>("\r\n"u8))
            {
                RespParsingException.ThrowUnexpectedToken(*(ptr - 2));
            }

            return true;
        }

        /// <inheritdoc cref="RespReadUtils.TryReadErrorAsString(out string, ref byte*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadErrorAsString(out string error, ref byte* ptr, byte* end)
            => RespReadUtils.TryReadErrorAsString(out error, ref ptr, end);

        /// <summary>
        /// Read string array with length header
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadStringArrayWithLengthHeader(out string[] result, ref byte* ptr, byte* end)
        {
            result = null;

            // Parse RESP array header
            if (!RespReadUtils.TryReadSignedArrayLength(out var length, ref ptr, end))
            {
                return false;
            }

            if (length < 0)
            {
                // NULL value ('*-1\r\n')
                return true;
            }

            // Parse individual strings in the array
            result = new string[length];
            for (var i = 0; i < length; i++)
            {
                if (*ptr == '$')
                {
                    if (!TryReadStringWithLengthHeader(out result[i], ref ptr, end))
                        return false;
                }
                else if (*ptr == '+')
                {
                    if (!TryReadSimpleString(out result[i], ref ptr, end))
                        return false;
                }
                else if (*ptr == '*')
                {
                    if (!TryReadStringArrayWithLengthHeader(out var subArray, ref ptr, end))
                        return false;
                    result[i] = string.Join(", ", subArray);
                }
                else
                {
                    if (!TryReadIntegerAsString(out result[i], ref ptr, end))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Read string array with length header
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadStringArrayWithLengthHeader(MemoryPool<byte> pool, out MemoryResult<byte>[] result, ref byte* ptr, byte* end)
        {
            result = null;
            // Parse RESP array header
            if (!RespReadUtils.TryReadSignedArrayLength(out var length, ref ptr, end))
            {
                return false;
            }

            if (length < 0)
            {
                // NULL value ('*-1\r\n')
                return true;
            }

            // Parse individual strings in the array
            result = new MemoryResult<byte>[length];
            for (var i = 0; i < length; i++)
            {
                if (*ptr == '$')
                {
                    if (!TryReadStringWithLengthHeader(pool, out result[i], ref ptr, end))
                        return false;
                }
                else if (*ptr == '+')
                {
                    if (!TryReadSimpleString(pool, out result[i], ref ptr, end))
                        return false;
                }
                else
                {
                    if (!TryReadIntegerAsString(pool, out result[i], ref ptr, end))
                        return false;
                }
            }

            return true;
        }

        /// <inheritdoc cref="RespReadUtils.TryReadInt32WithLengthHeader(out int, ref byte*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadIntWithLengthHeader(out int number, ref byte* ptr, byte* end)
            => RespReadUtils.TryReadInt32WithLengthHeader(out number, ref ptr, end);

        /// <summary>
        /// Read ASCII string without header until string terminator ('\r\n').
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadString(MemoryPool<byte> pool, out MemoryResult<byte> result, ref byte* ptr, byte* end)
        {
            result = default;
            if (ptr + 1 >= end)
                return false;

            var start = ptr;
            while (ptr < end - 1)
            {
                if (*(ushort*)ptr == MemoryMarshal.Read<ushort>("\r\n"u8))
                {
                    result = MemoryResult<byte>.Create(pool, (int)(ptr - start));
                    new ReadOnlySpan<byte>(start, result.Length).CopyTo(result.Span);
                    ptr += 2;
                    return true;
                }
                ptr++;
            }

            return false;
        }
    }
}