{
  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { flake-utils, nixpkgs, ... }:
  flake-utils.lib.eachDefaultSystem (
    system:
    let
      pkgs = import nixpkgs {
        inherit system;
      };
      deps = with pkgs; [
        dotnetCorePackages.sdk_8_0
      ];
    in
      {
        devShells.default = pkgs.mkShell {

          nativeBuildInputs = with pkgs; [
            nix-ld
          ] ++ deps;

          NIX_LD_LIBRARY_PATH = with pkgs; lib.makeLibraryPath ([
            stdenv.cc.cc
          ] ++ deps);
          
          LD_LIBRARY_PATH = with pkgs; lib.makeLibraryPath ([
            stdenv.cc.cc
            fontconfig
            xorg.libX11
            xorg.libICE
            xorg.libSM
          ] ++ deps);

          NIX_LD = "${pkgs.stdenv.cc.libc_bin}/bin/ld.so";
        };
      });
}
