using SciencePotato.Scripts.Common.Domain;
using System;

public class LinearTask(float progress, float target, string id, string type, bool isCompleted, long uid) : IProgressTask
{
	public float Progress { get; set; } = progress;

	public float Target { get; } = target;

	public string Id { get; } = id;

	public string Type { get;} = type;	

	public bool IsCompleted { get; set; } = isCompleted;
	
	public long UId { get; } = uid;

	public event Action OnCompleted;

	public void OnTick(float delta)
	{
		if (IsCompleted) return;
		Progress+=delta;
		if (Progress >= Target) IsCompleted = true;
		if(IsCompleted) OnCompleted?.Invoke();
	}

	public TaskSnapshot GetSnapshot()
	{
		return new TaskSnapshot
		{
			Progress = Progress,
			Target = Target,
			Id = Id,
			Type = Type,
			IsCompleted = IsCompleted
		};
	}
}
