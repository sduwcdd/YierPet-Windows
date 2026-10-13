namespace YierPet.Windows.Core;

public enum PetState
{
    Idle,
    RunningRight,
    RunningLeft,
    Waving,
    Jumping,
    Failed,
    Waiting,
    Running,
    Review
}

public static class PetStateInfo
{
    public static int Row(this PetState state) => state switch
    {
        PetState.Idle => 0,
        PetState.RunningRight => 1,
        PetState.RunningLeft => 2,
        PetState.Waving => 3,
        PetState.Jumping => 4,
        PetState.Failed => 5,
        PetState.Waiting => 6,
        PetState.Running => 7,
        PetState.Review => 8,
        _ => 0
    };

    public static IReadOnlyList<int> Durations(this PetState state) => state switch
    {
        PetState.Idle => [1500, 100, 140, 1300],
        PetState.RunningRight or PetState.RunningLeft => [130, 130, 130, 130],
        PetState.Waving => [200, 200, 200, 320],
        PetState.Jumping => [160, 130, 200, 260],
        PetState.Failed => [320, 260, 260, 400],
        PetState.Waiting => [500, 350, 350, 700],
        PetState.Running => [180, 180, 180, 250],
        PetState.Review => [600, 400, 650, 600],
        _ => [280]
    };

    public static string DisplayName(this PetState state) => state switch
    {
        PetState.Idle => "待机",
        PetState.RunningRight => "向右跑",
        PetState.RunningLeft => "向左跑",
        PetState.Waving => "挥手",
        PetState.Jumping => "跳跃",
        PetState.Failed => "沮丧",
        PetState.Waiting => "等待",
        PetState.Running => "工作中",
        PetState.Review => "审阅",
        _ => state.ToString()
    };
}
