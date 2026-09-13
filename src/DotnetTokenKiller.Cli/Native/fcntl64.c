/*
 * glibc < 2.28 has no fcntl64, which the statically linked SQLite calls; on 64-bit Linux it is the same call
 * as fcntl. Linked into the linux-x64 and linux-arm64 Native AOT binaries only (see DotnetTokenKiller.Cli.csproj).
 */
#include <stdarg.h>

extern int fcntl(int fd, int cmd, ...);

int fcntl64(int fd, int cmd, ...)
{
    va_list ap;
    va_start(ap, cmd);
    void *arg = va_arg(ap, void *);
    va_end(ap);
    return fcntl(fd, cmd, arg);
}
