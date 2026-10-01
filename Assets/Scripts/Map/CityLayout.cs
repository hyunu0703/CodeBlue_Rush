using System;
using UnityEngine;

/// <summary>Seed로 외곽 순환도로와 내부 가로세로 도로의 정수 격자 구조를 계산한다</summary>
public sealed class CityLayout
{
    private readonly byte[] masks;
    public int Width { get; }
    public int Height { get; }
    public int Seed { get; }
    public int Density { get; }
    public const int Version = 1;

    // 크기와 밀도를 검증하고 동일 순서의 정수 난수로 도로 구조를 생성한다
    public CityLayout(int seed, int width, int height, int density)
    {
        if (width < 5 || width > 24 || height < 5 || height > 24 || density < 0 || density > 100)
            throw new ArgumentOutOfRangeException(nameof(width), "도시 크기는 5~24, 내부 도로 밀도는 0~100이어야 합니다.");
        Width = width;
        Height = height;
        Seed = seed;
        Density = density;
        masks = new byte[width * height];
        uint random = unchecked((uint)seed);
        AddRow(0);
        AddRow(height - 1);
        AddColumn(0);
        AddColumn(width - 1);

        // 교차 분기가 없는 독립 방향 순환을 막기 위해 내부 가로세로 도로를 하나씩 보장한다
        int requiredRow = 1 + Next(ref random, height - 2);
        int requiredColumn = 1 + Next(ref random, width - 2);
        for (int y = 1; y < height - 1; y++)
        {
            int roll = Next(ref random, 100);
            if (y == requiredRow || roll < density)
                AddRow(y);
        }
        for (int x = 1; x < width - 1; x++)
        {
            int roll = Next(ref random, 100);
            if (x == requiredColumn || roll < density)
                AddColumn(x);
        }
    }

    // 북 동 남 서 비트로 저장한 연결 방향을 O(1)로 조회한다
    public int GetMask(int x, int y)
    {
        return x >= 0 && y >= 0 && x < Width && y < Height ? masks[y * Width + x] : 0;
    }

    // 북 동 남 서 인덱스를 격자 이동량으로 변환한다
    public static Vector2Int Direction(int index)
    {
        switch (index)
        {
            case 0: return Vector2Int.up;
            case 1: return Vector2Int.right;
            case 2: return Vector2Int.down;
            case 3: return Vector2Int.left;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    // 가로 도로의 양쪽 연결 비트를 동시에 기록한다
    private void AddRow(int y)
    {
        for (int x = 0; x < Width - 1; x++)
        {
            masks[y * Width + x] |= 2;
            masks[y * Width + x + 1] |= 8;
        }
    }

    // 세로 도로의 양쪽 연결 비트를 동시에 기록한다
    private void AddColumn(int x)
    {
        for (int y = 0; y < Height - 1; y++)
        {
            masks[y * Width + x] |= 1;
            masks[(y + 1) * Width + x] |= 4;
        }
    }

    // 런타임과 플랫폼의 Random 구현에 의존하지 않는 LCG를 진행한다
    private static int Next(ref uint state, int limit)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return (int)((state >> 8) % (uint)limit);
    }
}
