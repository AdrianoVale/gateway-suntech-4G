namespace GatewaySunteh4G_NET8.Services;

public interface ICommandDispatcher
{
    void PollAndDispatch(bool firstPoll);
    void RetryPendingCommandForDevice(string deviceId);
    /// <summary>
    /// Chamado quando o dispositivo envia uma nova posição (STT/ALT) após um comando.
    /// Se o comando estiver em status 3 (enviado, aguardando confirmação), confirma imediatamente (→ 2).
    /// Para outros status, aplica a lógica de retry normal.
    /// </summary>
    void ConfirmPendingCommandOnNewPosition(string deviceId);
    void HandleResponse(string deviceId, string commandCode1, string commandCode2, string? extraInfo = null);
}