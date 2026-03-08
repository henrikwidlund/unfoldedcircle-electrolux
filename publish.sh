#!/bin/bash

# Remove old unfolded-circle-electrolux.tar.gz if it exists
rm -f ./unfolded-circle-electrolux.tar.gz

# Remove old publish directory if it exists
rm -rf ./publish

# Clean build and publish directories using dotnet clean
echo "Clean"
dotnet clean -c Release -p:BuildForLinuxArm=true

# Run dotnet publish
echo "Publish main app"
dotnet publish ./src/UnfoldedCircle.Electrolux/UnfoldedCircle.Electrolux.csproj -c Release -p:BuildForLinuxArm=true -o ./publish

echo "Publish launcher"
dotnet publish ./src/UnfoldedCircle.Electrolux.Launcher/UnfoldedCircle.Electrolux.Launcher.csproj -c Release -p:BuildForLinuxArm=true -o ./publish-launcher

# Enter the publish directory
cd ./publish || exit

# Create a new directory called driver
mkdir -p driverdir

# Create bin, config, and data folders in the driver directory
mkdir -p ./driverdir/bin ./driverdir/config ./driverdir/data

# Modify driver.json with the current date and version
date=$(date -u +"%Y-%m-%d")
unprefixed_version=$(date -u +"%Y.%m.%d")
jq --arg version "$unprefixed_version" --arg date "$date" \
  '.version = $version | .release_date = $date' \
  ./driver.json > tmp.json && mv tmp.json ./driver.json

# Copy driver.json to the root of the driver directory
cp ./driver.json ./driverdir/

# Copy icon to root of the driver directory
cp ../electrolux.png ./driverdir/

# Copy appsettings*.json to the bin directory
cp ./appsettings*.json ./driverdir/bin/

# Copy both launcher (driver) and main app (app) to bin directory
cp ../publish-launcher/driver ./driverdir/bin/driver
cp ./driver ./driverdir/bin/app
cp ./driver.dbg ./driverdir/bin/app.dbg 2>/dev/null || true
cp ./*.pdb ./driverdir/bin/ 2>/dev/null || true

# Set permissions
chmod 755 ./driverdir/bin/driver
chmod 755 ./driverdir/bin/app

# Package the driver directory into a tarball
cd ./driverdir || exit
tar -czvf ../../unfolded-circle-electrolux.tar.gz ./*

# Remove the output directory
rm -rf ../../publish