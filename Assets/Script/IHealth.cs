public interface IHealth
{
    // マネージャーから強制的に生き返らせるための共通コマンド
    void ForceRevive(float healthValue);
}