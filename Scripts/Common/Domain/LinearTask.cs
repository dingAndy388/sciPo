using SciencePotato.Scripts.Common.Domain;
using System;

public class LinearTask : IProgressTask
{
	private readonly string _mapId;
	private readonly int _ownerId;

	public float Progress { get; set; }
	public float Target { get; }
	public string Id { get; }
	public string Type { get; }
	public bool IsCompleted { get; set; }
	public string UId { get; }

	public event Action OnCompleted;

	public LinearTask(float progress, float target, string id, string type, bool isCompleted, string uid, string mapId, int ownerId)
	{
		Progress = progress;
		Target = target;
		Id = id;
		Type = type;
		IsCompleted = isCompleted;
		UId = uid;
		_mapId = mapId;
		_ownerId = ownerId;
	}

	public void OnTick(float delta)
	{
		if (IsCompleted) return;
		Progress += delta;
		if (Progress >= Target) IsCompleted = true;
		if (IsCompleted) OnCompleted?.Invoke();
	}

	public TaskSnapshot GetSnapshot()
	{
		return new TaskSnapshot
		{
			MapId = _mapId,
			OwnerId = _ownerId,
			Progress = Progress,
			Target = Target,
			Id = Id,
			Type = Type,
			UId = UId,
			IsCompleted = IsCompleted
		};
	}
}