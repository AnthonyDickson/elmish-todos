{
  description = "F# development environment";

  inputs = {
    flake-parts.url = "github:hercules-ci/flake-parts";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs =
    { flake-parts, ... }@inputs:
    flake-parts.lib.mkFlake { inherit inputs; } {
      perSystem =
        { pkgs, ... }:
        {
          devShells.default = pkgs.mkShell {
            buildInputs = with pkgs; [
              # .NET / F#
              dotnet-sdk_10
              fsautocomplete

              # Markdown formatting
              dprint
            ];

            shellHook = ''
              echo "F# dev shell"
              echo "  dotnet $(dotnet --version)"

              # Restore local dotnet tools (FAKE, Femto, etc.)
              if [ -f .config/dotnet-tools.json ]; then
                dotnet tool restore
              fi
            '';

            env.DOTNET_ROOT = "${pkgs.dotnet-sdk_10}";
          };
        };

      flake = { };

      systems = [
        "x86_64-linux"
        "aarch64-linux"
        "x86_64-darwin"
        "aarch64-darwin"
      ];
    };
}

