namespace ChildDev.Api.Models.Dtos;

public record RegisterRequest(string NickName, string PinHash);
public record TokenRequest(string NickName, string PinHash);
public record AuthResponse(string Jwt, string AccountGuid);
