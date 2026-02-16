using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using System;

public partial class ServiceContainer : Node
{
	//Static Instance
	public static ServiceContainer Instance { get; private set; }

	//Services
	public MapQueryService mapQuery { get; private set; }
	public MapModificationService MapMod { get; private set; }

	//Interfaces
	private IRandom _random;

	public override void _Ready()
	{
		mapQuery = new MapQueryService();
		MapMod = new MapModificationService();
	}
}
