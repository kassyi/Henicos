# Phase 3 Vision: C# to GPU Source Generator (Transpiler)

## 概要 (Overview)
現在の `GpuBigInt` は、C#から文字列ベースの中間言語（IL）を発行し、それをCUDA側で解釈して実行する仕組みです。しかし、将来的な拡張性や開発体験（DX）を考慮すると、**「純粋なC#のコードを書くだけで、コンパイル時に自動的にGPUコード（CUDA / PTX）にトランスパイルされる別アセット（ライブラリ）」** の構築が理想的なゴールとなります。

## 目指す開発体験 (DX)
プログラマは、GPUのメモリ管理やレジスタの枯渇などを意識することなく、C#の標準的な構文（`for`, `while`, `var` など）を使ってアルゴリズムを記述できます。

```csharp
[GpuKernel]
public static void FermatTest(GpuInt T, GpuInt MaskR, int E, int bit2E1)
{
    // 自動でテンポラリ変数が割り当てられる
    var a = 10;
    
    // 標準的なforループがGPUのループ命令に変換される
    for (int i = 0; i < E; i++)
    {
        T = T.Square();
        for (int pass = 0; pass < 3; pass++)
        {
            var Q = T >> bit2E1;
            var R = T & MaskR;
            T = R + (Q << E) - Q - (Q << 1) - (Q << 3);
        }
    }
}
```

## アーキテクチャ構想 (Architecture)

### 1. C# Source Generator (Roslyn)
C# 9.0から導入された **Source Generator** を活用し、コンパイル時にRoslyn構文木（Syntax Tree）を解析します。
- `for` 文や `while` 文のSyntaxノードを抽出し、ASTを構築。
- `var` によるローカル変数宣言を検知し、暗黙的にレジスタ（TempX）を割り当て。

### 2. レジスタアロケーションと静的解析
GPUの最大の弱点である「レジスタの枯渇（Spilling）」をコンパイル時に防ぎます。
- ライフタイム解析（Liveness Analysis）を行い、不要になった変数のレジスタを自動解放・再利用。
- 使用するレジスタ数がGPUのハードウェア上限（例: 255個など）を超える場合は、ビルド時にVisual Studio上で **コンパイルエラー（赤い波線）** として開発者に警告を出します。

### 3. JIT / AOT トランスパイル
生成されたASTをもとに、バックエンド（CUDA C++、あるいはPTXアセンブリ、または現在の文字列表現IL）をSource Generatorが自動で出力し、C#アセンブリに埋め込みます。

## なぜこのアセットが重要か
現在、巨大な整数演算（多倍長演算）をGPUで実行できる使いやすいライブラリは世の中にほぼ存在しません。この「C# to GPU トランスパイラ」を独立したライブラリ（別アセット）として切り出し、OSS化すれば、暗号技術（ZKPなど）や巨大素数探索界隈で非常に価値のあるツールとなります。

## 次のステップ
現在のStep 2（NTTの開発によるE=43Mの突破）が完了し、強力な乗算バックエンドが確立したのち、**Phase 3** としてこのトランスパイラの開発に着手します。
