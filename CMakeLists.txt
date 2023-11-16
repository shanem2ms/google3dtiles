cmake_minimum_required(VERSION 3.15.0 FATAL_ERROR)
set(CMAKE_PREFIX_PATH ${PROJ_INSTALL_PATH} ${VCPKG_INSTALL_PATH})
set(CMAKE_SYSTEM_VERSION 10.0 CACHE STRING "" FORCE)
set(CMAKE_EXPORT_COMPILE_COMMANDS on)

project(libglb C CXX)
set(PROJECT_NAME glb)
set(LIB_NAME lib${PROJECT_NAME})
set(EXE_NAME ${PROJECT_NAME}app)
set(CMAKE_CONFIGURATION_TYPES "Debug;Release") 

set(SourceFiles
	"glb.cpp")

add_library(${LIB_NAME} SHARED ${SourceFiles})
add_executable(${EXE_NAME} ${SourceFiles})

find_path(TINYGLTF_INCLUDE_DIRS "tiny_gltf.h" HINTS ${CMAKE_CURRENT_SOURCE_DIR}/tinygltf REQUIRED)
message(${TINYGLTF_INCLUDE_DIRS})
target_include_directories(${LIB_NAME} PRIVATE ${TINYGLTF_INCLUDE_DIRS})
target_include_directories(${EXE_NAME} PRIVATE ${TINYGLTF_INCLUDE_DIRS})
find_package(draco CONFIG REQUIRED)
target_link_libraries(${LIB_NAME} PRIVATE draco::draco)
target_link_libraries(${EXE_NAME} PRIVATE draco::draco)