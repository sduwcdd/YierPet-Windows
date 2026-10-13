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

}
