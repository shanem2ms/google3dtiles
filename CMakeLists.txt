cmake_minimum_required(VERSION 3.15.0 FATAL_ERROR)
set(CMAKE_PREFIX_PATH ${PROJ_INSTALL_PATH} ${VCPKG_INSTALL_PATH})
set(CMAKE_SYSTEM_VERSION 10.0 CACHE STRING "" FORCE)
set(CMAKE_EXPORT_COMPILE_COMMANDS on)

project(glb C CXX)
set(PROJECT_NAME glb)
set(CMAKE_CONFIGURATION_TYPES "Debug;Release") 

set(SourceFiles
"glb.cpp"
)

add_executable(${PROJECT_NAME} ${SourceFiles})

find_path(TINYGLTF_INCLUDE_DIRS "tiny_gltf.h")
target_include_directories(${PROJECT_NAME} PRIVATE ${TINYGLTF_INCLUDE_DIRS})
