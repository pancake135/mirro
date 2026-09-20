namespace Mirro.Core
{
    /// <summary>한 판의 종류. Versus는 사람끼리(4인 멀티), 나머지는 혼자 하기 모드.</summary>
    public enum GameMode
    {
        Versus = 0,
        Bots = 1,
        Treasure = 2,
        Practice = 3
    }
}
