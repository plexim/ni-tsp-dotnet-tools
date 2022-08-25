import os
import subprocess
import time
import platform
import zipfile
import re
import shutil

repo_dir = "ni-tsp-dotnet-tools"  # Default repository name
src_dir = repo_dir+r"""\src"""
msbuild_dir = r"""C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin"""
output_dir = r""".\release"""

def print_info(msg):
    print ('<.> ' + msg)
    
def print_error(msg):
    print ('<!> ' + msg)

def configure_environment():
    if ((os.name == 'nt') and (platform.system() == 'Windows' )):
        return True
      
    elif((os.name == 'posix') and (platform.system() == 'Darwin' )):                
        return True 
    
    return False
    
def build_application():
    with open("build_application.log",'w') as output:
        p = subprocess.run("\""+msbuild_dir+"\msbuild.exe\" " + src_dir + r"\dotNetTools.sln -target:Clean,Build -p:Configuration=Release -p:Platform=x64", stdout=output,stderr=output)
    if p.returncode != 0:
        print_error("Unable to execute msbuild. Check log.")
        return False;  
    return True

def make_archive():
    #Copy from built model
    mv_src_dir = src_dir+r"""\ExtModeServer\bin\x64\Release""" 
    mv_output_dir = output_dir
    if os.path.isdir(output_dir):
        shutil.rmtree(output_dir)
    os.mkdir(output_dir)

    #Move dir excluding debug files. Also exclude NI Specific DLLs.
    for f in os.listdir(mv_src_dir):
        do_not_copy_re = (".*\.pdb|.*\.xml|" 
                          "National.*\.dll|ASAM.*\.dll|" 
                          "MdlWrapExe\.exe|Antlr3\.Runtime\.dll")
        if not re.match(do_not_copy_re,f): 
            mv_src_file = os.path.join(mv_src_dir,f)
            mv_dest_file = os.path.join(mv_output_dir,f)   
            shutil.move(mv_src_file,mv_dest_file)

    #Add versioning info
    print_info("Adding release info to output dir")
    os.chdir('./'+repo_dir)
    short_ver_tag = subprocess.check_output(["git", "describe", "--tag", "--always", "HEAD"]).strip().decode('utf-8')
    os.chdir("../")
            
    # create version file    
    with open(os.path.join(mv_output_dir,'version.txt'), "w") as f:
        f.write("Release Number: " + short_ver_tag + '\n')
        f.write("Build Date: "+ time.strftime("%c") + '\n')        
   
    # make zip
    assert os.path.isdir(output_dir)
    shutil.make_archive("dnetTools_"+short_ver_tag,'zip',output_dir)

    return True


def main():
    if not configure_environment():
        print_error("Unable to configure environment")
        return
    
    if ((os.name == 'posix') and (platform.system() == 'Darwin' )):
        print_error("Project generation requires Windows.  Terminating.")
        return True

    res = True

    print_info("")
    print_info("!!!!!!Remember to update tags prior to final deployment!!!!!")
    print_info("e.g.  git tag -a v0.1 -m \"version 0.1.1")
    print_info("      git push origin --tags")

    print_info("")
    print_info("Building application...")
    res=build_application() 
    if not res:
        return
    print_info("Success.")

    print_info("")
    print_info("Making zip archive...")
    res=make_archive() 
    if not res:
        return
    print_info("Success.")

if __name__ == '__main__':
    main()

