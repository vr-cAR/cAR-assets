SYSTEM = macosx_x64

TOOLS_DIR = tools
PROTO_DIR = cAR-proto

PROTOC = ${TOOLS_DIR}/${SYSTEM}/protoc
CSHARP_PLUGIN = ${TOOLS_DIR}/${SYSTEM}/grpc_csharp_plugin

SERVICES = Control Input
GEN_FILES = $(addsuffix .cs, ${SERVICES}) $(addsuffix Grpc.cs, ${SERVICES})
PROTOS = $(addprefix ${PROTO_DIR}/, $(addsuffix .proto, ${SERVICES}))
all: ${GEN_FILES}

${GEN_FILES}: ${PROTOS}
	${PROTOC} --csharp_out=. --grpc_out=. --plugin=protoc-gen-grpc=${CSHARP_PLUGIN} ${PROTOS}

clean:
	rm ${GEN_FILES}
